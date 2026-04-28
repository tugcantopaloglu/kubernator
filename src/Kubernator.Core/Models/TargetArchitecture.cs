namespace Kubernator.Core.Models;

public enum TargetArchitecture
{
    Unknown,
    X64,
    Arm64,
    X86,
    Arm
}

public enum TargetOs
{
    Unknown,
    Linux,
    LinuxMusl,
    Windows,
    Osx,
    Any
}
