using System.Windows.Forms;
using Oops.Core;
using Oops.Hooks;
using Oops.Settings;

namespace Oops;

/// <summary>
/// Главный координатор.
///
/// Модель работы — «расширяющаяся область», без единой догадки и БЕЗ буфера обмена:
///   - приложение ведёт собственную ленту набранных символов (TypingBuffer);
///   - 1-е нажатие хоткея правит последнее слово, 2-е — два последних, 3-е — три…;
///   - каждый шаг это преобразование 1-в-1 чётко очерченного куска;
///   - текст переписывается через Backspace + SendInput(Unicode), clipboard не трогаем вообще.
///
/// Два хоткея с одинаковой логикой области: смена раскладки и смена регистра.
/// Третий — перевод: он области не расширяет (перевод не сохраняет длину и не
/// обратим), а работает одним шагом по всей ленте или выделению.
/// </summary>
public sealed class App : IDisposable
{
    public AppSettings Settings { get; }

    /// <summary>
    /// Пока открыто окно настроек, хоткеи не работают.
    /// Без этого диалог записи невозможно использовать: нажатие текущего хоткея
    /// перехватывалось бы хуком, тот запускал бы чтение выделения, а оно шлёт
    /// Ctrl+C — и диалог записывал бы именно Ctrl+C вместо нажатого сочетания.
    /// </summary>
    public bool HotkeysSuspended { get; set; }

    private readonly TypingBuffer _buffer = new();
    private readonly ScopeEditor _scope = new();
    private readonly LayoutTracker _layoutTracker = new();
    private readonly KeyboardHook _kbHook = new();
    private readonly MouseHook _mouseHook = new();
    private readonly ForegroundWatcher _fgWatcher = new();

    private readonly SynchronizationContext _uiContext;

    /// <summary>Перевод уже идёт: повторные нажатия игнорируем.</summary>
    private bool _translating;

    private readonly Recorder _recorder = new();

    /// <summary>Идёт распознавание уже записанного — второе нажатие ждёт.</summary>
    private bool _transcribing;

    /// <summary>Запись включилась (true) или выключилась (false) — трею есть что показать.</summary>
    public event EventHandler<bool>? VoiceRecordingChanged;

    /// <summary>Промежуточная расшифровка, пока человек ещё говорит.</summary>
    public event EventHandler<string>? VoicePartial;

    /// <summary>Речь закончилась, идёт последний проход распознавания.</summary>
    public event EventHandler? VoiceRecognising;

    /// <summary>Всё закончено — плашку можно убирать.</summary>
    public event EventHandler? VoiceFinished;

    // Потоковое распознавание: пока идёт запись, раз в секунду прогоняем всё
    // записанное с начала фразы и дописываем разницу. Whisper не умеет
    // «продолжать» — он каждый раз распознаёт кусок целиком и может ПЕРЕДУМАТЬ
    // насчёт уже сказанного, поэтому напечатанное сравнивается с новым по
    // общему началу: расходящийся хвост стирается и печатается заново.
    private readonly System.Windows.Forms.Timer _voiceTick = new() { Interval = 1000 };
    private bool _partialBusy;
    private string _voiceTyped = string.Empty;
    private CancellationTokenSource? _voiceCts;

    /// <summary>Меньше секунды звука распознавать бессмысленно — только шум.</summary>
    private const int MinVoiceBytes = 44 + Recorder.SampleRate * 2;

    /// <summary>Нажали хоткей голосового ввода, а модели на диске нет.</summary>
    public event EventHandler? VoiceModelMissing;

    /// <summary>Микрофон или распознавание отказали.</summary>
    public event EventHandler<Exception>? VoiceFailed;

    /// <summary>Нажали хоткей перевода, а моделей на диске нет.</summary>
    public event EventHandler? TranslationModelsMissing;

    /// <summary>Движок перевода не смог отработать — показать человеку, а не молчать.</summary>
    public event EventHandler<Exception>? TranslationFailed;

    public App(AppSettings settings)
    {
        Settings = settings;
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("App must be created on the UI thread");

        ApplySettings();

        // Потолок записи упёрли — останавливаемся сами и печатаем, что успели.
        // Молча оборвать запись значило бы потерять всё сказанное.
        _recorder.LimitReached += (_, _) => _uiContext.Post(_ => StopVoice(), null);
        _voiceTick.Tick += OnVoiceTick;

        _kbHook.KeyDown += OnKeyDown;
        _mouseHook.Clicked += (_, _) => ResetAll();
        _fgWatcher.ForegroundChanged += (_, _) =>
        {
            ResetAll();
            _layoutTracker.Reset();
        };

        _kbHook.Install();
        _mouseHook.Install();
        _fgWatcher.Install();
    }

    public void ApplySettings()
    {
        _buffer.IdleTimeout = TimeSpan.FromSeconds(Settings.BufferIdleTimeoutSeconds);
        _scope.ExpandWindow = TimeSpan.FromSeconds(Settings.ExpandWindowSeconds);
        Sender.UseCharByChar(Settings.CharByCharTyping);
        _recorder.MaxDuration = TimeSpan.FromSeconds(Settings.VoiceMaxSeconds);
    }

    private void ResetAll()
    {
        _buffer.Clear();
        _scope.ResetSession();
    }

    private void OnKeyDown(object? sender, KeyboardHook.KeyEvent e)
    {
        if (!Settings.Enabled || HotkeysSuspended) return;

        // Пользователь сам сменил раскладку (Alt+Shift, Win+Space) — дальнейшие
        // нажатия дают другие буквы, наша лента больше не соответствует экрану.
        // Наши собственные переключения после конвертации сюда не попадают.
        if (_layoutTracker.UserChangedLayout())
            ResetAll();

        // Момент нажатия фиксируем здесь, а не внутри RunStep: там мы сперва ждём,
        // пока пользователь отпустит модификаторы, и печатаем текст посимвольно.
        // Окно расширения должно мерить ритм нажатий пользователя, а не нашу
        // собственную задержку, иначе на длинных словах область перестаёт расти.
        var pressedAtUtc = DateTime.UtcNow;

        if (Settings.ConvertHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win))
        {
            e.Handled = true;
            // Автоповтор удерживаемой клавиши — не новое нажатие. Без этой проверки
            // одно удержание Ctrl+Win даёт десятки шагов подряд.
            if (e.IsRepeat) return;
            // Пока Alt ещё зажат — гасим активацию строки меню, иначе уедет фокус.
            Sender.CancelMenuActivation();
            _uiContext.Post(_ => RunStep(layout: true, pressedAtUtc), null);
            return;
        }

        if (Settings.ChangeCaseHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win))
        {
            e.Handled = true;
            if (e.IsRepeat) return;
            Sender.CancelMenuActivation();
            _uiContext.Post(_ => RunStep(layout: false, pressedAtUtc), null);
            return;
        }

        if (Settings.TranslateHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win))
        {
            e.Handled = true;
            if (e.IsRepeat) return;
            Sender.CancelMenuActivation();
            _uiContext.Post(_ => RunTranslate(), null);
            return;
        }

        if (Settings.VoiceHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win))
        {
            e.Handled = true;
            if (e.IsRepeat) return;
            Sender.CancelMenuActivation();
            _uiContext.Post(_ => ToggleVoice(), null);
            return;
        }

        // Любое сочетание с Ctrl или Alt — это команда приложению (Ctrl+A, Ctrl+Z,
        // Ctrl+V, Ctrl+X…), она может изменить текст как угодно, и наша лента
        // перестаёт отражать экран. Особенно важен Ctrl+A: без сброса лента
        // осталась бы непустой, мы пошли бы в режим области, и первый же Backspace
        // удалил бы всё выделение целиком, а следом напечаталось бы одно слово.
        //
        // Сами модификаторы при этом не сбрасывают: наши хоткеи modifier-only
        // (Ctrl+Win, Alt+Win) срабатывают именно на нажатие модификатора, и сброс
        // по голому Ctrl убил бы их до того, как нажат Win.
        if ((e.Ctrl || e.Alt) && !IsModifierKey(e.VirtualKey))
        {
            ResetAll();
            return;
        }

        switch (e.VirtualKey)
        {
            case Keys.Back:
                _buffer.Backspace();
                _scope.ResetSession();
                return;

            // Любая навигация и завершение ввода — мы теряем связь с экраном.
            case Keys.Delete:
            case Keys.Left:
            case Keys.Right:
            case Keys.Home:
            case Keys.End:
            case Keys.Up:
            case Keys.Down:
            case Keys.PageUp:
            case Keys.PageDown:
            case Keys.Enter:
            case Keys.Tab:
            case Keys.Escape:
                ResetAll();
                return;
        }

        if (e.TypedChar.HasValue)
        {
            _buffer.Append(e.TypedChar.Value);
            _scope.ResetSession(); // новый ввод прерывает расширение области
        }
    }

    private static bool IsModifierKey(Keys k) =>
        k is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
          or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
          or Keys.Menu or Keys.LMenu or Keys.RMenu
          or Keys.LWin or Keys.RWin;

    private void RunStep(bool layout, DateTime pressedAtUtc)
    {
        // Ждём, пока пользователь отпустит модификаторы хоткея: иначе зажатый Ctrl
        // превратит наши Backspace в Ctrl+Backspace (удаление слова целиком),
        // а Ctrl+C для чтения выделения уйдёт как Ctrl+Alt+C и не сработает.
        Sender.WaitForModifiersReleased();
        Sender.ReleaseHotkeyModifiers();

        // Если лента не пуста — пользователь только что печатал, работаем по ней.
        // Выделение пробуем только когда лента пуста, и это не компромисс, а
        // следствие: выделить текст можно либо мышью, либо Shift+стрелками, а оба
        // действия ленту очищают. То есть «есть выделение» ⇒ «лента пуста».
        //
        // Порядок важен ещё и потому, что в новом Notepad (UWP) Ctrl+C без выделения
        // копирует всю текущую строку — проба выделения при непустой ленте
        // приняла бы эту строку за выделение и переписала бы её целиком.
        if (_buffer.Length > 0) ConvertBufferScope(layout, pressedAtUtc);
        else TryConvertSelection(layout);
    }

    /// <summary>
    /// Если в активном окне есть выделение — преобразует его целиком и печатает
    /// поверх (выделение ещё активно, ввод его перетирает). Возвращает false,
    /// если выделения нет и нужно работать с набранным буфером.
    /// </summary>
    private bool TryConvertSelection(bool layout)
    {
        var selection = SelectionReader.TryRead();
        if (string.IsNullOrEmpty(selection)) return false;

        string converted;
        var dir = LayoutConverter.Direction.None;
        if (layout) (converted, dir) = LayoutConverter.AutoConvertWithDirection(selection);
        else converted = ScopeEditor.ToggleCase(selection);

        if (converted != selection)
        {
            Sender.SendUnicode(converted);
            SwitchSystemLayout(dir);
        }

        // Мы переписали не то, что вели в ленте — она больше не отражает экран.
        ResetAll();
        return true;
    }

    /// <summary>Шаг расширяющейся области по набранному тексту.</summary>
    private void ConvertBufferScope(bool layout, DateTime pressedAtUtc)
    {
        var text = _buffer.Snapshot();
        if (text.Length == 0) return;

        var edit = layout
            ? _scope.NextLayoutStep(text, pressedAtUtc)
            : _scope.NextCaseStep(text, pressedAtUtc);

        if (edit.IsEmpty) return;

        Sender.SendBackspaces(edit.EraseCount);
        Sender.SendUnicode(edit.Text);

        SwitchSystemLayout(edit.Direction);

        // Лента теперь соответствует тому, что на экране.
        _buffer.Reset(edit.NewBufferContent);
    }

    /// <summary>
    /// Перевод набранного текста или выделения — одним шагом, без расширения
    /// области.
    ///
    /// Расширения здесь нет по существу задачи: перевод не сохраняет длину и
    /// не обратим, второе нажатие переводило бы уже переведённое. Границу
    /// задаёт то же правило, что и везде: есть лента — переводим её, лента
    /// пуста — пробуем выделение.
    ///
    /// Работа идёт в фоновом потоке: первая загрузка модели занимает сотни
    /// миллисекунд, а держать столько UI-поток нельзя — на нём же висит
    /// обработчик хука, и Windows снимает хук по таймауту.
    /// </summary>
    private void RunTranslate()
    {
        // Второе нажатие, пока идёт перевод, ничего не ускорит, а вот напечатать
        // результат дважды поверх самого себя вполне может.
        if (_translating) return;

        Sender.WaitForModifiersReleased();
        Sender.ReleaseHotkeyModifiers();

        var source = _buffer.Length > 0 ? _buffer.Snapshot() : SelectionReader.TryRead();
        if (string.IsNullOrWhiteSpace(source)) return;

        // Стираем ровно столько, сколько сами вели в ленте. Выделение стирать не
        // надо: оно ещё активно, и ввод перетирает его сам.
        int erase = _buffer.Length > 0 ? source.Length : 0;

        if (!Translator.IsReady)
        {
            ResetAll();
            TranslationModelsMissing?.Invoke(this, EventArgs.Empty);
            return;
        }

        _translating = true;
        var text = source!;
        Task.Run(() =>
        {
            try { return (Result: Translator.Translate(text), Error: (Exception?)null); }
            catch (Exception ex) { return (Result: (string?)null, Error: ex); }
        }).ContinueWith(t =>
        {
            var (result, error) = t.Result;
            _uiContext.Post(_ =>
            {
                _translating = false;
                if (error != null || string.IsNullOrEmpty(result))
                {
                    if (error != null) TranslationFailed?.Invoke(this, error);
                    return;
                }
                if (result == text) return;

                Sender.WaitForModifiersReleased();
                if (erase > 0) Sender.SendBackspaces(erase);
                Sender.SendUnicode(result!);

                // Напечатали не то, что вели в ленте, — она больше не отражает экран.
                ResetAll();
            }, null);
        });
    }

    /// <summary>
    /// Голосовой ввод: нажали — идёт запись, нажали ещё раз — записанное
    /// распознаётся и печатается в активное поле.
    ///
    /// Именно переключатель, а не «держать нажатым»: диктовать фразу, удерживая
    /// три клавиши, невозможно физически, а хук к тому же не отличает удержание
    /// от автоповтора без отдельного учёта.
    /// </summary>
    private void ToggleVoice()
    {
        if (_recorder.IsRecording) { StopVoice(); return; }
        if (_transcribing) return;

        if (!VoiceInput.IsReady)
        {
            VoiceModelMissing?.Invoke(this, EventArgs.Empty);
            return;
        }

        Sender.WaitForModifiersReleased();
        Sender.ReleaseHotkeyModifiers();

        try
        {
            _voiceTyped = string.Empty;
            _voiceCts = new CancellationTokenSource();
            _recorder.Start();
            VoiceRecordingChanged?.Invoke(this, true);
            if (Settings.VoiceLiveText) _voiceTick.Start();
        }
        catch (Exception ex)
        {
            VoiceFailed?.Invoke(this, ex);
        }
    }

    /// <summary>
    /// Промежуточный проход, пока человек говорит. Если предыдущий ещё считает —
    /// пропускаем такт: очередь из проходов только отстанет от речи.
    /// </summary>
    private async void OnVoiceTick(object? sender, EventArgs e)
    {
        if (_partialBusy || !_recorder.IsRecording) return;

        var wav = _recorder.Snapshot();
        if (wav == null || wav.Length < MinVoiceBytes) return;

        _partialBusy = true;
        try
        {
            var token = _voiceCts?.Token ?? CancellationToken.None;
            var text = await Task.Run(() => VoiceInput.TranscribeAsync(wav, token));
            // Пока считали, запись могли остановить — тогда последнее слово за
            // финальным проходом, а не за этим.
            if (_recorder.IsRecording) ApplyVoiceText(text);
        }
        catch
        {
            // Промежуточный проход не обязан удаваться: финальный всё исправит.
        }
        finally
        {
            _partialBusy = false;
        }
    }

    /// <summary>
    /// Приводит напечатанное к новой расшифровке: общее начало не трогаем,
    /// расходящийся хвост стираем и печатаем заново. Стираем ровно столько,
    /// сколько напечатали сами, — чужой текст в поле не страдает.
    /// </summary>
    private void ApplyVoiceText(string text)
    {
        text ??= string.Empty;
        VoicePartial?.Invoke(this, text);
        if (text == _voiceTyped) return;

        int common = 0;
        while (common < text.Length && common < _voiceTyped.Length
               && text[common] == _voiceTyped[common]) common++;

        Sender.WaitForModifiersReleased();
        if (_voiceTyped.Length > common) Sender.SendBackspaces(_voiceTyped.Length - common);
        if (text.Length > common) Sender.SendUnicode(text[common..]);
        _voiceTyped = text;

        // Напечатали не то, что вели в ленте, — она больше не отражает экран.
        ResetAll();
    }

    private void StopVoice()
    {
        if (!_recorder.IsRecording) return;

        _voiceTick.Stop();
        var wav = _recorder.Stop();
        VoiceRecordingChanged?.Invoke(this, false);
        if (wav == null) return;

        VoiceRecognising?.Invoke(this, EventArgs.Empty);
        _transcribing = true;
        Task.Run(async () =>
        {
            try { return (Text: await VoiceInput.TranscribeAsync(wav), Error: (Exception?)null); }
            catch (Exception ex) { return (Text: (string?)null, Error: ex); }
        }).ContinueWith(t =>
        {
            var (text, error) = t.Result;
            _uiContext.Post(_ =>
            {
                _transcribing = false;
                if (error != null)
                {
                    VoiceFinished?.Invoke(this, EventArgs.Empty);
                    VoiceFailed?.Invoke(this, error);
                    return;
                }

                // Пустая расшифровка при уже напечатанном тексте — не повод
                // стирать напечатанное: молчание в конце фразы обычное дело.
                if (!string.IsNullOrWhiteSpace(text) || _voiceTyped.Length == 0)
                    ApplyVoiceText(text ?? string.Empty);
                _voiceTyped = string.Empty;
                VoiceFinished?.Invoke(this, EventArgs.Empty);
            }, null);
        });
    }

    /// <summary>
    /// Переключает системную раскладку и помечает смену как нашу, чтобы
    /// LayoutTracker не принял её за ручное переключение пользователем
    /// и не сбросил ленту — иначе следующее нажатие хоткея не смогло бы
    /// расширить область.
    /// </summary>
    private void SwitchSystemLayout(LayoutConverter.Direction dir)
    {
        if (dir == LayoutConverter.Direction.None) return;

        _layoutTracker.NoteSelfSwitch();
        if (dir == LayoutConverter.Direction.ToRu) LayoutSwitcher.SwitchToRussian();
        else LayoutSwitcher.SwitchToEnglish();
    }

    public void Dispose()
    {
        _voiceTick.Stop();
        _voiceTick.Dispose();
        _voiceCts?.Cancel();
        _voiceCts?.Dispose();
        _recorder.Dispose();
        VoiceInput.Unload();
        Translator.Unload();
        _kbHook.Dispose();
        _mouseHook.Dispose();
        _fgWatcher.Dispose();
    }
}
