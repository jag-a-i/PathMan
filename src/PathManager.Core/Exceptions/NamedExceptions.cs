using System;

namespace PathManager.Core.Exceptions;

public class PathManagerException : Exception
{
    public PathManagerException(string message) : base(message) { }
    public PathManagerException(string message, Exception innerException) : base(message, innerException) { }
}

public class TargetNotFoundException : PathManagerException
{
    public string TargetPath { get; }
    public TargetNotFoundException(string targetPath) 
        : base($"Target file not found: '{targetPath}'.")
    {
        TargetPath = targetPath;
    }
}

public class UnsupportedRunnableException : PathManagerException
{
    public string Extension { get; }
    public UnsupportedRunnableException(string extension) 
        : base($"Don't know how to run '{extension}' files. Hint: specify a host with --host <path_to_interpreter>.")
    {
        Extension = extension;
    }
}

public class InvalidCommandNameException : PathManagerException
{
    public string CommandName { get; }
    public InvalidCommandNameException(string commandName, string reason) 
        : base($"Illegal command name '{commandName}': {reason}")
    {
        CommandName = commandName;
    }
}

public class NameCollisionException : PathManagerException
{
    public string CommandName { get; }
    public string ExistingTarget { get; }
    public NameCollisionException(string commandName, string existingTarget) 
        : base($"Command '{commandName}' is already linked to '{existingTarget}'. Use --force to overwrite.")
    {
        CommandName = commandName;
        ExistingTarget = existingTarget;
    }
}

public class ShadowCollisionException : PathManagerException
{
    public string CommandName { get; }
    public string ExistingLocation { get; }
    public ShadowCollisionException(string commandName, string existingLocation) 
        : base($"Command '{commandName}' is already present on PATH at '{existingLocation}'. Linking it could cause unexpected shadowing. Hint: use 'pathman why {commandName}' to trace resolution.")
    {
        CommandName = commandName;
        ExistingLocation = existingLocation;
    }
}

public class ShimWriteFailedException : PathManagerException
{
    public ShimWriteFailedException(string message, Exception? innerException = null) 
        : base($"Could not write shim executable: {message}", innerException!) { }
}

public class CatalogPersistFailedException : PathManagerException
{
    public CatalogPersistFailedException(string message, Exception? innerException = null) 
        : base($"Catalog save failed; no changes were linked: {message}", innerException!) { }
}

public class CatalogLockedException : PathManagerException
{
    public CatalogLockedException() 
        : base("Catalog is locked by another process. Please retry in a moment.") { }
}

public class PathWriteFailedException : PathManagerException
{
    public PathWriteFailedException(string message, Exception? innerException = null) 
        : base($"User PATH registry update failed: {message}. Hint: check permissions or run 'pathman doctor'.", innerException!) { }
}

public class PathHardLimitException : PathManagerException
{
    public int AttemptedLength { get; }
    public PathHardLimitException(int attemptedLength) 
        : base($"User PATH would exceed the Win32 limit of 32,767 characters (attempted length: {attemptedLength}). Write refused to prevent corruption.")
    {
        AttemptedLength = attemptedLength;
    }
}

public class ElevationRequiredException : PathManagerException
{
    public ElevationRequiredException(string action) 
        : base($"Elevation required: '{action}' requires administrator privileges.") { }
}

public class ProfileWriteFailedException : PathManagerException
{
    public string ProfilePath { get; }
    public string Snippet { get; }
    public ProfileWriteFailedException(string profilePath, string snippet, Exception? innerException = null) 
        : base($"Failed to write completion hook to profile '{profilePath}'. Please add the following snippet manually:\n\n{snippet}", innerException!)
    {
        ProfilePath = profilePath;
        Snippet = snippet;
    }
}

public class ExecutionPolicyBlockedException : PathManagerException
{
    public ExecutionPolicyBlockedException() 
        : base("PowerShell script execution is blocked by ExecutionPolicy. Hint: run 'Set-ExecutionPolicy RemoteSigned -Scope CurrentUser'.") { }
}

public class NothingToUndoException : PathManagerException
{
    public NothingToUndoException() 
        : base("No snapshot found to undo.") { }
}

public class UndoConflictException : PathManagerException
{
    public UndoConflictException(string message) 
        : base($"Cannot safely undo: {message}") { }
}

public class StaleTargetException : PathManagerException
{
    public string TargetPath { get; }
    public StaleTargetException(string commandName, string targetPath) 
        : base($"{commandName}: target file is missing or inaccessible: '{targetPath}'.")
    {
        TargetPath = targetPath;
    }
}

public class HostNotFoundException : PathManagerException
{
    public string HostName { get; }
    public HostNotFoundException(string commandName, string hostName) 
        : base($"{commandName}: required host interpreter '{hostName}' was not found on PATH.")
    {
        HostName = hostName;
    }
}

public class InsecureTargetException : PathManagerException
{
    public string TargetPath { get; }
    public InsecureTargetException(string targetPath) 
        : base($"Security warning: target '{targetPath}' is located in a world-writable directory. Use --i-trust-this to link anyway.")
    {
        TargetPath = targetPath;
    }
}
