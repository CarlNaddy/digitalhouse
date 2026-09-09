namespace DigitalHouse.Features.Files;

/// <summary>
/// File storage settings (parity plan P4.4), bound from config section
/// <c>FileStorage</c>. <see cref="Provider"/> is the config-driven swap
/// point <c>Program.cs</c> reads to choose an <see cref="IFileStore"/>
/// implementation — only <c>LocalDisk</c> exists today; a blob (Azure/S3)
/// provider is a later addition, not a later rewrite.
/// </summary>
public class FileStorageOptions
{
    public string Provider { get; set; } = "LocalDisk";

    /// <summary>Root directory for <see cref="LocalDiskFileStore"/>, relative to the content root.</summary>
    public string RootPath { get; set; } = "App_Data/uploads";
}
