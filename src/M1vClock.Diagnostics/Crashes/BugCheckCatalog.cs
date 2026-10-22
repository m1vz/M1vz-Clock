namespace M1vClock.Diagnostics.Crashes;

public static class BugCheckCatalog
{
    private static readonly Dictionary<uint, string> Names = new()
    {
        [0x0A] = "IRQL_NOT_LESS_OR_EQUAL",
        [0x19] = "BAD_POOL_HEADER",
        [0x1A] = "MEMORY_MANAGEMENT",
        [0x1E] = "KMODE_EXCEPTION_NOT_HANDLED",
        [0x24] = "NTFS_FILE_SYSTEM",
        [0x3B] = "SYSTEM_SERVICE_EXCEPTION",
        [0x3F] = "NO_MORE_SYSTEM_PTES",
        [0x4E] = "PFN_LIST_CORRUPT",
        [0x50] = "PAGE_FAULT_IN_NONPAGED_AREA",
        [0x51] = "REGISTRY_ERROR",
        [0x77] = "KERNEL_STACK_INPAGE_ERROR",
        [0x7A] = "KERNEL_DATA_INPAGE_ERROR",
        [0x7B] = "INACCESSIBLE_BOOT_DEVICE",
        [0x7E] = "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",
        [0x7F] = "UNEXPECTED_KERNEL_MODE_TRAP",
        [0x9F] = "DRIVER_POWER_STATE_FAILURE",
        [0xA0] = "INTERNAL_POWER_ERROR",
        [0xA5] = "ACPI_BIOS_ERROR",
        [0xBE] = "ATTEMPTED_WRITE_TO_READONLY_MEMORY",
        [0xC2] = "BAD_POOL_CALLER",
        [0xC4] = "DRIVER_VERIFIER_DETECTED_VIOLATION",
        [0xC5] = "DRIVER_CORRUPTED_EXPOOL",
        [0xD1] = "DRIVER_IRQL_NOT_LESS_OR_EQUAL",
        [0xEA] = "THREAD_STUCK_IN_DEVICE_DRIVER",
        [0xEF] = "CRITICAL_PROCESS_DIED",
        [0xF4] = "CRITICAL_OBJECT_TERMINATION",
        [0x101] = "CLOCK_WATCHDOG_TIMEOUT",
        [0x109] = "CRITICAL_STRUCTURE_CORRUPTION",
        [0x116] = "VIDEO_TDR_FAILURE",
        [0x117] = "VIDEO_TDR_TIMEOUT_DETECTED",
        [0x119] = "VIDEO_SCHEDULER_INTERNAL_ERROR",
        [0x124] = "WHEA_UNCORRECTABLE_ERROR",
        [0x133] = "DPC_WATCHDOG_VIOLATION",
        [0x139] = "KERNEL_SECURITY_CHECK_FAILURE",
        [0x13A] = "KERNEL_MODE_HEAP_CORRUPTION",
        [0x141] = "VIDEO_ENGINE_TIMEOUT_DETECTED",
        [0x154] = "UNEXPECTED_STORE_EXCEPTION",
        [0x161] = "LIVE_SYSTEM_DUMP",
        [0x1CA] = "SYNTHETIC_WATCHDOG_TIMEOUT",
    };

    public static IReadOnlyCollection<string> AllNames { get; } = Names.Values.Order(StringComparer.Ordinal).ToList();

    public static string? NameOf(uint code) => Names.GetValueOrDefault(code);
}

public static class ExceptionCatalog
{
    private static readonly Dictionary<uint, string> Names = new()
    {
        [0x80000003] = "BREAKPOINT",
        [0xC0000005] = "ACCESS_VIOLATION",
        [0xC000001D] = "ILLEGAL_INSTRUCTION",
        [0xC0000094] = "INT_DIVIDE_BY_ZERO",
        [0xC00000FD] = "STACK_OVERFLOW",
        [0xC0000135] = "DLL_NOT_FOUND",
        [0xC0000142] = "DLL_INIT_FAILED",
        [0xC0000374] = "HEAP_CORRUPTION",
        [0xC0000409] = "STACK_BUFFER_OVERRUN",
        [0xC000007B] = "INVALID_IMAGE_FORMAT",
        [0xE0434352] = "CLR_EXCEPTION",
        [0xE06D7363] = "CPP_EXCEPTION",
    };

    public static IReadOnlyCollection<string> AllNames { get; } = Names.Values.Order(StringComparer.Ordinal).ToList();

    public static string? NameOf(uint code) => Names.GetValueOrDefault(code);
}
