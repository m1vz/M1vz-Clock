using System.Globalization;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;

namespace M1vClock.Core.Security;

public enum HardenedDirectoryStatus
{
    Created,
    Verified,
    Quarantined,
}

public static class HardenedDirectory
{
    private const FileSystemRights WriteRights =
        FileSystemRights.WriteData
        | FileSystemRights.AppendData
        | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes
        | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions
        | FileSystemRights.TakeOwnership;

    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    public static HardenedDirectoryStatus Ensure(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full) ?? throw new ArgumentException("A pasta precisa ter uma pasta pai.", nameof(path));
        RejectReparsePoint(new DirectoryInfo(parent));
        return EnsureChild(full);
    }

    public static HardenedDirectoryStatus EnsureChild(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new DirectoryInfo(Path.GetFullPath(path));
        if (!info.Exists)
        {
            info.Create(CreateSecurity());
            info.Refresh();
            RejectReparsePoint(info);
            return HardenedDirectoryStatus.Created;
        }

        RejectReparsePoint(info);
        if (IsTrusted(info))
        {
            return HardenedDirectoryStatus.Verified;
        }

        Quarantine(info);
        new DirectoryInfo(info.FullName).Create(CreateSecurity());
        return HardenedDirectoryStatus.Quarantined;
    }

    public static bool IsTrusted(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var security = directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !IsPrivileged(owner))
        {
            return false;
        }

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            if (rule.IdentityReference is SecurityIdentifier sid && IsPrivileged(sid))
            {
                continue;
            }

            if ((rule.FileSystemRights & WriteRights) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPrivileged(SecurityIdentifier sid) => sid.Equals(Administrators) || sid.Equals(LocalSystem);

    private static void RejectReparsePoint(DirectoryInfo directory)
    {
        if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new SecurityException($"A pasta {directory.FullName} é um link ou junção e não pode ser usada.");
        }
    }

    private static void Quarantine(DirectoryInfo directory)
    {
        using (TokenPrivileges.Enable("SeTakeOwnershipPrivilege", "SeRestorePrivilege"))
        {
            var ownerOnly = new DirectorySecurity();
            ownerOnly.SetOwner(Administrators);
            directory.SetAccessControl(ownerOnly);
            directory.SetAccessControl(CreateSecurity());
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var target = $"{directory.FullName}.untrusted-{stamp}";
        directory.MoveTo(target);
    }

    private static DirectorySecurity CreateSecurity()
    {
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(Administrators);
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }
}
