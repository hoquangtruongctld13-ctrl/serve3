namespace subphimv1
{
    public enum OcrMode { GoogleCloud, GeminiApi }
    public enum VsfVideoOpenMethod { Default, OpenCV, FFmpeg }
    public enum VsfProcessingMode { CleanAndCreateTxtImages, SearchSubtitlesOnly }
    public enum CurrentViewMode { OcrProcessing, SrtTranslation }
    public enum TextEdgeStyle { None, Outline, Shadow }
    public enum SrtApiProvider
    {
        ChutesAI,
        Gemini,
        ChatGPT,
        AIOLauncher,
        AIOLauncherVIP
    }
}