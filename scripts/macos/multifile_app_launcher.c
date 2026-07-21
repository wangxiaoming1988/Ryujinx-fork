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

    char runtime_path[PATH_MAX];
    int length = snprintf(
        runtime_path,
        sizeof(runtime_path),
        "%s/../Resources/runtime/Ryujinx",
        dirname(resolved_path));

    if (length < 0 || (size_t)length >= sizeof(runtime_path))
    {
        fputs("Ryujinx runtime path is too long.\n", stderr);
        return 127;
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
