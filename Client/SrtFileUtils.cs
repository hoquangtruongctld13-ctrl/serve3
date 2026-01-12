using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;


namespace subphimv1.Subphim
{
    public static class SrtFileUtils
    {
        private static readonly Regex AssTagRegex = new Regex(@"\{[^\}]*\}", RegexOptions.Compiled);
        public static string GenerateSrtContent(List<SrtSubtitleLine> lines, bool useTranslatedText)
        {
            var sb = new StringBuilder();
            foreach (var line in lines.OrderBy(l => l.Index))
            {
                sb.AppendLine(line.Index.ToString());
                sb.AppendLine(line.TimeCode);

                string textToWrite = (useTranslatedText && !string.IsNullOrWhiteSpace(line.TranslatedText))
                                     ? line.TranslatedText
                                     : line.OriginalText;
                string plainText = textToWrite.Replace(@"\N", Environment.NewLine);

                sb.AppendLine(plainText);
                sb.AppendLine();
            }
            return sb.ToString();
        }
        private static string CleanAssTags(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            return AssTagRegex.Replace(text, string.Empty).Trim();
        }
        public static List<SrtSubtitleLine> CreateSrtLinesFromImageFiles(IEnumerable<string> imageFiles, TimeSpan timeOffset, int startIndex)
        {
            var srtLines = new List<SrtSubtitleLine>();
            int srtIdxCounter = startIndex;

            foreach (var imagePath in imageFiles)
            {
                string imgNameWithoutExt = Path.GetFileNameWithoutExtension(imagePath);
                var nameParts = imgNameWithoutExt.Split(new string[] { "__" }, StringSplitOptions.None);
                if (nameParts.Length >= 2)
                {
                    var startParts = nameParts[0].Split('_');
                    var endParts = nameParts[1].Split('_');

                    if (startParts.Length >= 4 && endParts.Length >= 4)
                    {
                        try
                        {
                            int sH = int.Parse(startParts[0]);
                            int sM = int.Parse(startParts[1]);
                            int sS = int.Parse(startParts[2]);
                            int sMs = int.Parse(startParts[3].PadRight(3, '0').Substring(0, 3));
                            var originalStartTime = new TimeSpan(0, sH, sM, sS, sMs);

                            int eH = int.Parse(endParts[0]);
                            int eM = int.Parse(endParts[1]);
                            int eS = int.Parse(endParts[2]);
                            int eMs = int.Parse(endParts[3].PadRight(3, '0').Substring(0, 3));
                            var originalEndTime = new TimeSpan(0, eH, eM, eS, eMs);
                            var originalDuration = (originalEndTime > originalStartTime) ? (originalEndTime - originalStartTime) : TimeSpan.FromSeconds(1);
                            var newStartTime = originalStartTime + timeOffset;
                            var srtLine = new SrtSubtitleLine
                            {
                                Index = srtIdxCounter++,
                                OriginalText = "[Đang chờ OCR...]",
                                ImagePath = imagePath,
                                StartTime = newStartTime,
                                Duration = originalDuration 
                            };

                            srtLines.Add(srtLine);

                        }
                        catch (FormatException) { /**/ }
                    }
                }
            }
            return srtLines;
        }
        public static List<SrtSubtitleLine> LoadFromFile(string filePath)
        {
            var srtLines = new List<SrtSubtitleLine>();
            var allFileLines = File.ReadAllLines(filePath, Encoding.UTF8);

            bool isAssFile = allFileLines.Any(line => line.Trim().StartsWith("[Events]", StringComparison.OrdinalIgnoreCase));

            if (isAssFile)
            {
                var dialogueLines = allFileLines
                    .Where(line => line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                int srtIndexCounter = 1;
                foreach (var line in dialogueLines)
                {
                    try
                    {
                        int commaCount = 0;
                        int lastCommaIndex = -1;
                        for (int i = 0; i < line.Length; i++)
                        {
                            if (line[i] == ',')
                            {
                                commaCount++;
                                if (commaCount == 9)
                                {
                                    lastCommaIndex = i;
                                    break;
                                }
                            }
                        }

                        if (lastCommaIndex == -1) continue;

                        string[] headerParts = line.Substring(0, lastCommaIndex).Split(',');
                        if (headerParts.Length < 3) continue;

                        string startTime = headerParts[1].Trim();
                        string endTime = headerParts[2].Trim();

                        string textContent = line.Substring(lastCommaIndex + 1).Trim();

                        // Chuẩn hóa các kiểu xuống dòng thành \N
                        string normalizedText = textContent.Replace(@"\n", @"\N");
                        string cleanedText = CleanAssTags(normalizedText);

                        var srtLine = new SrtSubtitleLine
                        {
                            Index = srtIndexCounter++,
                            OriginalText = cleanedText,
                        };
                        srtLine.SetTimeFromTimeCodeString($"{startTime} --> {endTime}");

                        if (!string.IsNullOrWhiteSpace(srtLine.OriginalText))
                        {
                            srtLines.Add(srtLine);
                        }
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }
            else // Logic cho SRT/VTT
            {
                var blocks = new List<List<string>>();
                var currentBlockLines = new List<string>();
                bool isVttFile = allFileLines.FirstOrDefault()?.Trim().ToUpperInvariant() == "WEBVTT";

                var linesToProcess = allFileLines.AsEnumerable();
                if (isVttFile)
                {
                    linesToProcess = linesToProcess.Skip(1);
                }

                foreach (var lineText in linesToProcess)
                {
                    if (string.IsNullOrWhiteSpace(lineText))
                    {
                        if (currentBlockLines.Any())
                        {
                            blocks.Add(new List<string>(currentBlockLines));
                            currentBlockLines.Clear();
                        }
                    }
                    else { currentBlockLines.Add(lineText); }
                }
                if (currentBlockLines.Any()) blocks.Add(currentBlockLines);

                int srtIndexCounter = 1;
                foreach (var block in blocks)
                {
                    if (!block.Any()) continue;
                    var srtLine = new SrtSubtitleLine();
                    int textStartIndex = 0;
                    if (isVttFile)
                    {
                        if (block[0].Contains("-->")) { textStartIndex = 1; }
                        else if (block.Count > 1 && block[1].Contains("-->")) { textStartIndex = 2; }
                        else continue;
                        srtLine.SetTimeFromTimeCodeString(block[textStartIndex - 1].Replace('.', ','));
                    }
                    else
                    {
                        if (int.TryParse(block[0], out _))
                        {
                            if (block.Count > 1 && block[1].Contains("-->")) { textStartIndex = 2; } else continue;
                            srtLine.SetTimeFromTimeCodeString(block[1]);
                        }
                        else if (block[0].Contains("-->"))
                        {
                            textStartIndex = 1;
                            srtLine.SetTimeFromTimeCodeString(block[0]);
                        }
                        else continue;
                    }
                    // Đọc text và chuyển đổi xuống dòng thật thành thẻ \N
                    string rawText = string.Join(@"\N", block.Skip(textStartIndex)).Trim();
                    srtLine.OriginalText = CleanAssTags(rawText);
                    srtLine.Index = srtIndexCounter++;
                    if (!string.IsNullOrWhiteSpace(srtLine.OriginalText))
                    {
                        srtLines.Add(srtLine);
                    }
                }
            }

            return srtLines;
        }
        public static void SaveToFile(string filePath, List<SrtSubtitleLine> lines)
        {
            string fileExtension = Path.GetExtension(filePath).ToLowerInvariant();
            var sb = new StringBuilder();

            if (fileExtension == ".vtt")
            {
                sb.AppendLine("WEBVTT");
                sb.AppendLine();
            }

            foreach (var line in lines.OrderBy(l => l.Index))
            {
                if (fileExtension != ".vtt")
                {
                    sb.AppendLine(line.Index.ToString());
                }
                string timeCodeForExport = fileExtension == ".vtt"
                    ? line.TimeCode.Replace(',', '.')
                    : line.TimeCode;
                sb.AppendLine(timeCodeForExport);

                // Lấy văn bản (dịch hoặc gốc)
                string textToWrite = string.IsNullOrWhiteSpace(line.TranslatedText) ? line.OriginalText : line.TranslatedText;

                // Chuyển đổi thẻ \N thành xuống dòng thật cho file SRT/VTT
                string plainText = textToWrite.Replace(@"\N", Environment.NewLine);

                sb.AppendLine(plainText);
                sb.AppendLine();
            }
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        public static List<SrtSubtitleLine> CreateSrtLinesFromImageFiles(IEnumerable<string> imageFiles)
        {
            var srtLines = new List<SrtSubtitleLine>();
            int srtIdxCounter = 1;

            foreach (var imagePath in imageFiles)
            {
                string imgNameWithoutExt = Path.GetFileNameWithoutExtension(imagePath);
                var nameParts = imgNameWithoutExt.Split(new string[] { "__" }, StringSplitOptions.None);
                if (nameParts.Length >= 2)
                {
                    var startParts = nameParts[0].Split('_');
                    var endParts = nameParts[1].Split('_');

                    if (startParts.Length >= 4 && endParts.Length >= 4)
                    {
                        try
                        {
                            int sH = int.Parse(startParts[0]);
                            int sM = int.Parse(startParts[1]);
                            int sS = int.Parse(startParts[2]);
                            string sMs = startParts[3].PadRight(3, '0').Substring(0, 3);
                            int eH = int.Parse(endParts[0]);
                            int eM = int.Parse(endParts[1]);
                            int eS = int.Parse(endParts[2]);
                            string eMs = endParts[3].PadRight(3, '0').Substring(0, 3);

                            string timeCode = $"{sH:D2}:{sM:D2}:{sS:D2},{sMs} --> {eH:D2}:{eM:D2}:{eS:D2},{eMs}";
                            var srtLine = new SrtSubtitleLine
                            {
                                Index = srtIdxCounter++,
                                OriginalText = "[Đang chờ OCR...]",
                                ImagePath = imagePath
                            };
                            srtLine.SetTimeFromTimeCodeString(timeCode);
                            srtLines.Add(srtLine);
                        }
                        catch (FormatException) { /* Bỏ qua các file có tên không đúng định dạng */ }
                    }
                }
            }
            return srtLines;
        }

        public static List<SrtSubtitleLine> MergeDuplicates(ObservableCollection<SrtSubtitleLine> lines)
        {
            var removedLines = new List<SrtSubtitleLine>();
            if (lines == null || lines.Count < 2)
            {
                return removedLines;
            }

            const double ADJACENT_THRESHOLD_MS = 1.0;

            // Chỉ xử lý các dòng phụ đề, không phải TextClip
            var subtitleLines = lines.Where(l => !l.IsTextClip).OrderBy(l => l.StartTime).ToList();

            if (subtitleLines.Count < 2)
            {
                return removedLines;
            }

            for (int i = subtitleLines.Count - 2; i >= 0; i--)
            {
                var currentLine = subtitleLines[i];
                var nextLine = subtitleLines[i + 1];

                bool isTextSame = !string.IsNullOrWhiteSpace(currentLine.OriginalText) &&
                                  currentLine.OriginalText.Equals(nextLine.OriginalText, StringComparison.Ordinal);

                if (!isTextSame)
                {
                    continue;
                }

                double timeGapMs = (nextLine.StartTime - currentLine.EndTime).TotalMilliseconds;
                bool isTimeAdjacent = timeGapMs >= 0 && timeGapMs <= ADJACENT_THRESHOLD_MS;

                if (isTextSame && isTimeAdjacent)
                {
                    // Gộp: Cập nhật EndTime của dòng hiện tại và đánh dấu dòng tiếp theo để xóa
                    currentLine.EndTime = nextLine.EndTime;

                    // Thêm dòng sẽ bị xóa vào danh sách trả về
                    removedLines.Add(nextLine);

                    // Xóa dòng đã gộp khỏi danh sách tạm thời để các vòng lặp sau không bị ảnh hưởng
                    subtitleLines.RemoveAt(i + 1);
                }
            }

            if (removedLines.Any())
            {
                // Xóa các dòng đã được gộp khỏi ObservableCollection gốc
                foreach (var lineToRemove in removedLines)
                {
                    lines.Remove(lineToRemove);
                }

                // Đánh lại số thứ tự cho toàn bộ danh sách còn lại
                ReIndex(lines);
            }
            else{}
            return removedLines;
        }

        public static int RemoveEmptyLines(ObservableCollection<SrtSubtitleLine> lines)
        {
            var linesToRemove = lines.Where(l => string.IsNullOrWhiteSpace(l.OriginalText) || l.OriginalText == "[Đang chờ OCR...]").ToList();
            if (linesToRemove.Any())
            {
                foreach (var line in linesToRemove) lines.Remove(line);
                ReIndex(lines);
            }
            return linesToRemove.Count;
        }

        public static void ReIndex(ObservableCollection<SrtSubtitleLine> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                lines[i].Index = i + 1;
            }
        }

        // === OVERLOAD FOR LIST (Thread-safe for batch processing) ===
        public static void ReIndex(List<SrtSubtitleLine> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                lines[i].Index = i + 1;
            }
        }

        // === OVERLOAD FOR LIST (Thread-safe for batch processing) ===
        // Returns list of merged lines, does not modify original list
        public static List<SrtSubtitleLine> MergeDuplicates(List<SrtSubtitleLine> lines)
        {
            var mergedLines = new List<SrtSubtitleLine>();
            if (lines == null || lines.Count < 2)
            {
                return mergedLines;
            }

            const double ADJACENT_THRESHOLD_MS = 1.0;

            // Chỉ xử lý các dòng phụ đề, không phải TextClip
            var subtitleLines = lines.Where(l => !l.IsTextClip).OrderBy(l => l.StartTime).ToList();

            if (subtitleLines.Count < 2)
            {
                return mergedLines;
            }

            for (int i = subtitleLines.Count - 2; i >= 0; i--)
            {
                var currentLine = subtitleLines[i];
                var nextLine = subtitleLines[i + 1];

                bool isTextSame = !string.IsNullOrWhiteSpace(currentLine.OriginalText) &&
                                  currentLine.OriginalText.Equals(nextLine.OriginalText, StringComparison.Ordinal);

                if (!isTextSame)
                {
                    continue;
                }

                double timeGapMs = (nextLine.StartTime - currentLine.EndTime).TotalMilliseconds;
                bool isTimeAdjacent = timeGapMs >= 0 && timeGapMs <= ADJACENT_THRESHOLD_MS;

                if (isTextSame && isTimeAdjacent)
                {
                    // Gộp: Cập nhật EndTime của dòng hiện tại và đánh dấu dòng tiếp theo
                    currentLine.EndTime = nextLine.EndTime;

                    // Thêm dòng sẽ bị xóa vào danh sách trả về
                    mergedLines.Add(nextLine);

                    // Xóa dòng đã gộp khỏi danh sách tạm thời
                    subtitleLines.RemoveAt(i + 1);
                }
            }

            return mergedLines;
        }

        public static string ProcessOcrText(string rawContent, OcrMode mode)
        {
            if (string.IsNullOrEmpty(rawContent)) return string.Empty;
            var lines = rawContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            bool isGoogleCloudOutput = mode == OcrMode.GoogleCloud && lines.Length > 0 &&
                                       !lines[0].Trim().Equals(GeminiOcrService.NO_TEXT_INDICATOR, StringComparison.OrdinalIgnoreCase);

            var relevantLines = isGoogleCloudOutput ? lines.Skip(1) : lines;
            var processedLines = relevantLines.Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l));
            string result = string.Join(@"\N", processedLines); 

            if (result.Equals(GeminiOcrService.NO_TEXT_INDICATOR, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return result;
        }
    }
}