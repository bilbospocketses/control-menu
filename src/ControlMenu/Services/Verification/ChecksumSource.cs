namespace ControlMenu.Services.Verification;

public enum ChecksumFormat { SqliteDownloadPage, InTotoJsonl, Sha256SumsFile }
public enum ChecksumAlgorithm { Sha256, Sha3_256 }

public record ChecksumSource(string UrlOrTemplate, ChecksumFormat Format, ChecksumAlgorithm Algorithm);
