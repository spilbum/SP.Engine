namespace SP.Shared.Resource;

public enum ErrorCode : int
{
    InternalError = -99,
    UnknownMsgId = -3,
    InvalidFormat = -2,
    Unknown = -1,
    Success = 0,
    ForceUpdateRequired= 1,
    NoServerAvailable = 2,
    Maintenance = 3,
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
    None = 0,
    Dev = 1,
    QA = 2,
    Stage = 3,
    Live = 4
}

public enum StoreType : byte
{
    None = 0,
    GooglePlay = 1,
    AppStore = 2,
    Steam = 3
}

public enum ColumnType
{
    String,
    Byte,
    Int32,
    Int64,
    Float,
    Double,
    Boolean,
    DateTime
}
