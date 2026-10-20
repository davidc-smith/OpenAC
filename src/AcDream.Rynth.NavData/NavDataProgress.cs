namespace AcDream.Rynth.NavData;

public enum NavDataStage { Downloading, Verified, Unpacking, Installing }

/// <summary>One progress report from an install: RynthCore's DownloadProgress, in our own type.</summary>
public sealed record NavDataProgress(NavDataStage Stage, double? Percent, string Detail);
