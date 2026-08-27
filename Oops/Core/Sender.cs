using System.Runtime.InteropServices;

namespace Oops.Core;

/// <summary>
/// Обёртка над SendInput для отправки Backspace и юникодных символов.
/// </summary>
public static class Sender
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_BACK = 0x08;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    /// <summary>Зарезервированный «ничей» код: нажатие есть, действия нет.</summary>
    private const ushort VK_NONAME = 0xFC;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL, wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int nVirtKey);

    /// <summary>
    /// Сколько символов (или Backspace) уходит в одной посылке SendInput.
    ///
    /// Раньше слали строго по одному с задержкой 15–25 мс, и фраза в тридцать
    /// символов переписывалась больше секунды: тридцать посылок на стирание плюс
    /// тридцать на печать. Причина осторожности настоящая — Electron и браузерные
    /// поля ввода теряют события из больших посылок, — но теряются именно БОЛЬШИЕ
    /// пачки, когда вся строка уходит одним вызовом. Десяток символов за раз
    /// такие приёмники переваривают, а время падает на порядок.
    ///
    /// Снятие модификаторов повторяется в КАЖДОЙ посылке, а не один раз перед
    /// циклом: пользователь может всё ещё держать хоткей, и автоповтор вернёт
    /// зажатый Alt обратно. Поэтому посылка должна оставаться короткой — между
    /// двумя очистками не должно проходить больше интервала автоповтора (~30 мс).
    ///
    /// Значение 1 возвращает прежнее поведение — на случай приложения, которое
    /// всё-таки давится пачками (настройка «Печатать посимвольно»).
    /// </summary>
    public static int ChunkSize { get; set; } = 8;

    /// <summary>Пауза между посылками.</summary>
    public static int ChunkDelayMs { get; set; } = 4;

    /// <summary>Прежний медленный режим: по одному символу с большой паузой.</summary>
    public static void UseCharByChar(bool on)
    {
        ChunkSize = on ? 1 : 8;
        ChunkDelayMs = on ? 20 : 4;
    }

    /// <summary>
    /// Нейтрализует «тап» модификатора. Если Windows видит Alt-down → Alt-up без
    /// клавиш между ними, она активирует строку меню окна; ровно так же Win-down →
    /// Win-up открывает «Пуск». В обоих случаях фокус уходит из поля ввода, и
    /// последующий SendUnicode печатает в никуда.
    ///
    /// Безобидный Ctrl-тап, вставленный пока модификатор ещё зажат, ломает этот
    /// шаблон: Alt-down → Ctrl → Alt-up меню не активирует. Ctrl без последующей
    /// буквы не делает ничего ни в одном приложении.
    ///
    /// Win проверяем не из перестраховки. Хоткей матчится на клавише, которая
    /// замкнула сочетание, и глотаем мы только её. Если Win нажали ПЕРВОЙ, её
    /// нажатие ушло в систему целым — сочетание тогда замыкает Alt, глотается он,
    /// а Windows видит одинокую Win и открывает «Пуск».
    /// </summary>
    public static void CancelMenuActivation()
    {
        bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
        bool winDown = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0
                    || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
        if (!altDown && !winDown) return;

        // Для Alt тапаем Ctrl — этот вариант здесь уже проверен и работает.
        // Для Win тапаем VK_NONAME: это зарезервированный «ничей» код, который
        // не делает ровно ничего, но считается нажатием клавиши и потому ломает
        // шаблон «Win нажали и отпустили, ничего между». Ctrl тут не годится —
        // Win+Ctrl уже часть системных сочетаний.
        var inputs = new List<INPUT>(4);
        if (altDown)
        {
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL } } });
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } });
        }
        if (winDown)
        {
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_NONAME } } });
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_NONAME, dwFlags = KEYEVENTF_KEYUP } } });
        }
        var arr = inputs.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Ждёт, пока пользователь физически отпустит все модификаторы хоткея.
    /// Без этого зажатый Ctrl превратит наши Backspace в Ctrl+Backspace (удаление
    /// слова целиком), а зажатый Alt/Win исказит ввод.
    /// </summary>
    public static void WaitForModifiersReleased(int maxMs = 1000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(maxMs);
        while (DateTime.UtcNow < deadline && AnyModifierDown())
            System.Threading.Thread.Sleep(15);
        System.Threading.Thread.Sleep(40); // даём системе доставить key-up'ы
    }

    private static bool AnyModifierDown() =>
        (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

    /// <summary>
    /// Снимает потенциально зажатые пользователем модификаторы хоткея (Ctrl/Shift/Alt/Win).
    /// Должно вызываться перед последовательностью бэкспейсов / Unicode-ввода,
    /// иначе зажатый Ctrl превратит Backspace в Ctrl+Backspace (удаление слова),
    /// а Win-down под Backspace может сработать как системный шорткат.
    /// </summary>
    public static void ReleaseHotkeyModifiers()
    {
        var inputs = new[]
        {
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_SHIFT,   dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_MENU,    dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_LWIN,    dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_RWIN,    dwFlags = KEYEVENTF_KEYUP } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        System.Threading.Thread.Sleep(20); // даём приложению переварить key-up'ы
    }

    /// <summary>Перемещает каретку вправо N раз (полезно перед SendBackspaces, если буфер-курсор не в конце).</summary>
    public static void SendRightArrow(int count)
    {
        if (count <= 0) return;
        const ushort VK_RIGHT = 0x27;
        var pair = new INPUT[2];
        pair[0] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_RIGHT } } };
        pair[1] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_RIGHT, dwFlags = KEYEVENTF_KEYUP } } };
        int sz = Marshal.SizeOf<INPUT>();
        for (int i = 0; i < count; i++)
        {
            SendInput(2, pair, sz);
            System.Threading.Thread.Sleep(20);
        }
    }

    /// <summary>
    /// Стирает <paramref name="count"/> символов слева от каретки.
    ///
    /// Перед КАЖДЫМ Backspace в той же посылке снимаются модификаторы. Это не
    /// перестраховка: если пользователь всё ещё физически держит хоткей, Windows
    /// продолжает автоповтором подтверждать нажатие модификатора, и Backspace
    /// уходит как Win+Backspace (в большинстве приложений — вообще ничего) или
    /// Ctrl+Backspace (удаление слова целиком). Одноразовой очистки перед циклом
    /// не хватает — зажатая клавиша возвращает состояние обратно.
    /// </summary>
    public static void SendBackspaces(int count)
    {
        if (count <= 0) return;

        int chunk = Math.Max(1, ChunkSize);
        int sz = Marshal.SizeOf<INPUT>();

        for (int sent = 0; sent < count; sent += chunk)
        {
            int len = Math.Min(chunk, count - sent);
            var batch = new INPUT[ModifierClears + len * 2];
            FillModifierClears(batch);
            for (int i = 0; i < len; i++)
            {
                batch[ModifierClears + i * 2] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_BACK } } };
                batch[ModifierClears + i * 2 + 1] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_BACK, dwFlags = KEYEVENTF_KEYUP } } };
            }
            SendInput((uint)batch.Length, batch, sz);
            System.Threading.Thread.Sleep(ChunkDelayMs);
        }
    }

    /// <summary>Сколько INPUT-ов в начале посылки занимает снятие модификаторов.</summary>
    private const int ModifierClears = 5;

    private static void FillModifierClears(INPUT[] batch)
    {
        batch[0] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } };
        batch[1] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_SHIFT,   dwFlags = KEYEVENTF_KEYUP } } };
        batch[2] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_MENU,    dwFlags = KEYEVENTF_KEYUP } } };
        batch[3] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_LWIN,    dwFlags = KEYEVENTF_KEYUP } } };
        batch[4] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_RWIN,    dwFlags = KEYEVENTF_KEYUP } } };
    }

    /// <summary>
    /// Отправляет строку как последовательность KEYEVENTF_UNICODE — работает в любой раскладке.
    ///
    /// Переводы строк нормализуются к одиночному CR: KEYEVENTF_UNICODE порождает
    /// WM_CHAR, а стандартные поля ввода трактуют как перенос именно 0x0D. Пара
    /// "\r\n" дала бы два переноса вместо одного.
    ///
    /// ЗАЖАТЫЙ ALT СНИМАЕТСЯ ПЕРЕД КАЖДЫМ СИМВОЛОМ — это не перестраховка.
    /// Windows решает, что послать окну, по состоянию именно Alt: при зажатом
    /// Alt вместо WM_CHAR уходит WM_SYSCHAR, а его поля ввода игнорируют (ещё и
    /// пищат). Символ просто пропадает. Ctrl на это не влияет — под Ctrl
    /// KEYEVENTF_UNICODE доходит как обычный WM_CHAR.
    ///
    /// Отсюда и разница, из-за которой хоткей раскладки (Ctrl+Win) работал, а
    /// регистра (Alt+Win) — нет: WaitForModifiersReleased ждёт отпускания не
    /// дольше секунды, и если человек держит аккорд дольше, ReleaseHotkeyModifiers
    /// не помогает — автоповтор удерживаемой клавиши возвращает Alt обратно.
    /// Бэкспейсы при этом проходили (там модификаторы снимаются в каждой посылке),
    /// поэтому со стороны это выглядело как «стёрло и ничего не написало» или,
    /// на пустом буфере, как полное отсутствие реакции.
    /// </summary>
    public static void SendUnicode(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        text = text.Replace("\r\n", "\r").Replace('\n', '\r');

        int sz = Marshal.SizeOf<INPUT>();
        int chunk = Math.Max(1, ChunkSize);

        for (int start = 0; start < text.Length; )
        {
            int len = Math.Min(chunk, text.Length - start);

            // Суррогатную пару нельзя разрывать между посылками: два её кода
            // должны уйти подряд, иначе приёмник получит два битых символа.
            if (len < text.Length - start && char.IsHighSurrogate(text[start + len - 1]))
                len++;

            // Гасим Alt (он решает WM_CHAR/WM_SYSCHAR) и обе Win — под зажатым Win
            // символ может уйти в системный шорткат. Ctrl и Shift не трогаем:
            // на KEYEVENTF_UNICODE они не влияют, а лишние события ввода — риск.
            var batch = new INPUT[3 + len * 2];
            batch[0] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_MENU, dwFlags = KEYEVENTF_KEYUP } } };
            batch[1] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_LWIN, dwFlags = KEYEVENTF_KEYUP } } };
            batch[2] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_RWIN, dwFlags = KEYEVENTF_KEYUP } } };

            for (int i = 0; i < len; i++)
            {
                ushort ch = text[start + i];
                batch[3 + i * 2] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE } } };
                batch[3 + i * 2 + 1] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } };
            }

            SendInput((uint)batch.Length, batch, sz);
            start += len;
            if (start < text.Length) System.Threading.Thread.Sleep(ChunkDelayMs);
        }
    }

    /// <summary>
    /// Эмуляция Ctrl+C / Ctrl+V. Перед основной комбинацией снимаем все "лишние"
    /// модификаторы (Shift/Alt/Win), которые мог удерживать пользователь от хоткея —
    /// иначе приложение увидит, например, Ctrl+Alt+C вместо чистого Ctrl+C и copy не сработает.
    /// </summary>
    public static void SendCtrlKey(char key)
    {
        ushort vk = (ushort)char.ToUpper(key);
        var inputs = new[]
        {
            // снимаем потенциально зажатые модификаторы
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_SHIFT, dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_MENU,  dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_LWIN,  dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_RWIN,  dwFlags = KEYEVENTF_KEYUP } } },
            // и саму комбинацию Ctrl+key
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
