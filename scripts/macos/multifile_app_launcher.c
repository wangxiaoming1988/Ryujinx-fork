#include <libgen.h>
#include <limits.h>
#include <mach-o/dyld.h>
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>

int main(int argc, char **argv)
{
    char executable_path[PATH_MAX];
    uint32_t executable_path_size = sizeof(executable_path);

    if (_NSGetExecutablePath(executable_path, &executable_path_size) != 0)
    {
        fputs("Ryujinx launcher path is too long.\n", stderr);
        return 127;
    }

    char resolved_path[PATH_MAX];

    if (realpath(executable_path, resolved_path) == NULL)
    {
        perror("Unable to resolve Ryujinx launcher path");
        return 127;
    }

    char launcher_directory[PATH_MAX];
    int length = snprintf(launcher_directory, sizeof(launcher_directory), "%s", resolved_path);

    if (length < 0 || (size_t)length >= sizeof(launcher_directory))
    {
        fputs("Ryujinx launcher directory is too long.\n", stderr);
        return 127;
    }

    char *launcher_directory_path = dirname(launcher_directory);
    char runtime_path[PATH_MAX];
    length = snprintf(
        runtime_path,
        sizeof(runtime_path),
        "%s/../Resources/runtime/Ryujinx",
        launcher_directory_path);

    if (length < 0 || (size_t)length >= sizeof(runtime_path))
    {
        fputs("Ryujinx runtime path is too long.\n", stderr);
        return 127;
    }

    char dotnet_root[PATH_MAX];
    length = snprintf(
        dotnet_root,
        sizeof(dotnet_root),
        "%s/../Resources/dotnet",
        launcher_directory_path);

    if (length < 0 || (size_t)length >= sizeof(dotnet_root))
    {
        fputs("Bundled .NET runtime path is too long.\n", stderr);
        return 127;
    }

    if (access(dotnet_root, F_OK) == 0)
    {
        setenv("DOTNET_ROOT", dotnet_root, 1);
        setenv("DOTNET_ROOT_ARM64", dotnet_root, 1);
    }

    char **runtime_argv = calloc((size_t)argc + 1, sizeof(char *));

    if (runtime_argv == NULL)
    {
        perror("Unable to allocate Ryujinx launcher arguments");
        return 127;
    }

    runtime_argv[0] = runtime_path;

    for (int index = 1; index < argc; index++)
    {
        runtime_argv[index] = argv[index];
    }

    execv(runtime_path, runtime_argv);
    perror("Unable to start Ryujinx runtime");

    free(runtime_argv);
    return 127;
}
