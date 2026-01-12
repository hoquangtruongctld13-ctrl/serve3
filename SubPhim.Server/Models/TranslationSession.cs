using SubPhim.Server.Data;
using System.Collections.Concurrent;

namespace SubPhim.Server.Models
{
    public class TranslationJob
    {
        public string SessionId { get; set; }
        public int UserId { get; set; }
        public string Genre { get; set; }
        public string TargetLanguage { get; set; }
        public ConcurrentQueue<SrtLineBatch> PendingBatches { get; set; }
        public ConcurrentBag<TranslatedSrtLine> TranslatedLines { get; set; }
        public CancellationTokenSource Cts { get; set; }
        public bool IsCompleted { get; set; } = false;
        public string ErrorMessage { get; set; }
    }

    public class SrtLineBatch
    {
        public List<SrtLine> Lines { get; set; }
    }

    public class SrtLine
    {
        public int Index { get; set; }
        public string OriginalText { get; set; }
    }

    public class TranslatedSrtLine
    {
        public int Index { get; set; }
        public string TranslatedText { get; set; }
        public bool Success { get; set; }
    }
}