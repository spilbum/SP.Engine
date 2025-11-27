namespace SP.Shared.Resource;

public enum ErrorCode : byte
{
    None = 0,
    InvalidFormat = 1,
    MissingField = 2,
    UnknownMsgId = 3,
    OutdatedSnapshot = 4,
    InternalError = 100,
}

public enum ServerStatus : byte
{
    Unknown = 0,
    Online = 1,
    Busy = 2,
    Full = 3,
    Offline = 4
}

public enum PlatformKind : byte
{
    Unknown = 0,
    Android = 1,
    iOS = 2,
    Windows = 3,
    MacOS = 4,
}

public enum ServerGroupType : byte
{
    Dev = 1,
    QA = 2,
    Stage = 3,
    Live = 4
}

public enum StoreType : byte
{
    Unknown = 0,
    GooglePlay = 1,
    AppStore = 2,
    Steam = 3
}
