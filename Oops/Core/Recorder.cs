using NAudio.Wave;

namespace Oops.Core;

/// <summary>
/// Запись с микрофона для голосового ввода.
///
/// Формат прибит гвоздями: 16 кГц, моно, 16 бит. Не выбор и не компромисс —
/// whisper.cpp принимает только это, любой другой формат пришлось бы
/// пересчитывать самим.
///
/// Звук живёт только в памяти и только до распознавания: на диск не пишется,
/// в сеть не уходит. Программа и так видит всё, что человек печатает, — писать
/// ещё и то, что он говорит, в файл рядом с настройками недопустимо.
/// </summary>
public sealed class Recorder : IDisposable
{
    public const int SampleRate = 16000;
    private const int Bits = 16;
    private const int Channels = 1;

    /// <summary>
    /// Потолок длины записи. Без него забытый включённым микрофон копит
    /// мегабайты в памяти, а распознавание такой записи занимает минуты.
    /// </summary>
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Запись остановилась сама, упершись в <see cref="MaxDuration"/>.</summary>
    public event EventHandler? LimitReached;

    private readonly object _gate = new();
    private WaveInEvent? _device;
    private MemoryStream? _pcm;

    public bool IsRecording { get { lock (_gate) return _device != null; } }

    /// <summary>Начинает запись. Бросает, если микрофона нет или он занят.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_device != null) return;
            if (WaveInEvent.DeviceCount == 0)
                throw new InvalidOperationException(L10n.T("voice.err.noMicrophone"));

            _pcm = new MemoryStream();
            var device = new WaveInEvent
            {
                WaveFormat = new WaveFormat(SampleRate, Bits, Channels),
                // Мельче буфер — быстрее реакция на «стоп»; крупнее — меньше
                // событий. 100 мс здесь ни на что не влияет, кроме задержки.
                BufferMilliseconds = 100,
            };
            device.DataAvailable += OnData;
            _device = device;
            device.StartRecording();
        }
    }

    /// <summary>
    /// Останавливает запись и отдаёт WAV целиком. null — записать ничего не
    /// успели (нажали второй раз мгновенно).
    /// </summary>
    public byte[]? Stop()
    {
        MemoryStream? pcm;
        lock (_gate)
        {
            if (_device == null) return null;
            _device.DataAvailable -= OnData;
            try { _device.StopRecording(); } catch { }
            _device.Dispose();
            _device = null;
            pcm = _pcm;
            _pcm = null;
        }

        if (pcm == null || pcm.Length == 0) return null;
        var wav = WrapInWav(pcm.ToArray());
        pcm.Dispose();
        return wav;
    }

    /// <summary>
    /// Копия записанного на текущий момент, БЕЗ остановки записи — для
    /// промежуточного распознавания, пока человек ещё говорит.
    /// </summary>
    public byte[]? Snapshot()
    {
        lock (_gate)
        {
            if (_pcm == null || _pcm.Length == 0) return null;
            return WrapInWav(_pcm.ToArray());
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        bool overflow = false;
        lock (_gate)
        {
            if (_pcm == null) return;
            _pcm.Write(e.Buffer, 0, e.BytesRecorded);
            overflow = _pcm.Length >= MaxBytes;
        }
        // Событие поднимаем ВНЕ замка: подписчик пойдёт останавливать запись,
        // а Stop() берёт тот же замок — получили бы взаимную блокировку прямо
        // на потоке звукового драйвера.
        if (overflow) LimitReached?.Invoke(this, EventArgs.Empty);
    }

    private long MaxBytes => (long)(MaxDuration.TotalSeconds * SampleRate * Channels * (Bits / 8));

    /// <summary>
    /// Дописывает к сырым сэмплам заголовок WAV: Whisper.net принимает поток
    /// с заголовком, а не голый PCM. Заголовок собран руками — 44 байта
    /// известного формата надёжнее зависимости от чужого API записи в поток.
    /// </summary>
    public static byte[] WrapInWav(byte[] pcm)
    {
        const int headerSize = 44;
        int byteRate = SampleRate * Channels * (Bits / 8);
        short blockAlign = (short)(Channels * (Bits / 8));

        var output = new byte[headerSize + pcm.Length];
        using (var writer = new BinaryWriter(new MemoryStream(output)))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + pcm.Length);      // размер файла минус первые 8 байт
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);                   // длина блока fmt для PCM
            writer.Write((short)1);             // PCM без сжатия
            writer.Write((short)Channels);
            writer.Write(SampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)Bits);
            writer.Write("data"u8);
            writer.Write(pcm.Length);
            writer.Write(pcm);
        }
        return output;
    }

    public void Dispose() => Stop();
}
