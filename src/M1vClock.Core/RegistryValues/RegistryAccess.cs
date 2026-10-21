using Microsoft.Win32;

namespace M1vClock.Core.RegistryValues;

public interface IRegistryAccess
{
    RegistryValueState Read(RegistryValueAddress address);

    void Write(RegistryValueAddress address, RegistryValueState state);
}

public sealed class WindowsRegistryAccess : IRegistryAccess
{
    public RegistryValueState Read(RegistryValueAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        using var root = RegistryKey.OpenBaseKey(address.Hive, address.View);
        using var key = root.OpenSubKey(address.KeyPath, writable: false);
        if (key is null)
        {
            return RegistryValueState.Absent(keyExists: false);
        }

        var value = key.GetValue(address.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value is null
            ? RegistryValueState.Absent(keyExists: true)
            : new RegistryValueState(true, true, key.GetValueKind(address.ValueName), value);
    }

    public void Write(RegistryValueAddress address, RegistryValueState state)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(state);
        using var root = RegistryKey.OpenBaseKey(address.Hive, address.View);

        if (state.ValueExists)
        {
            using var key = root.CreateSubKey(address.KeyPath, writable: true);
            key.SetValue(address.ValueName, state.Data ?? throw new ArgumentException("Valor sem dados.", nameof(state)), state.Kind);
            return;
        }

        using (var key = root.OpenSubKey(address.KeyPath, writable: true))
        {
            if (key is null)
            {
                return;
            }

            key.DeleteValue(address.ValueName, throwOnMissingValue: false);
            if (state.KeyExists || key.ValueCount > 0 || key.SubKeyCount > 0)
            {
                return;
            }
        }

        root.DeleteSubKey(address.KeyPath, throwOnMissingSubKey: false);
    }
}
