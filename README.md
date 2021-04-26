A prototype of a high speed cross platform GNU `find` replacement.

Goals:
----
* Efficent multi-threading.
* Dynamically compiles the find expression for increased execution speed.
* Cross platform and usage of latest available APIs.
* Fall back with native dotnet core API incase the platform does not have a custom written driver.
* Argument compatible with GNU `find`.

Findings/ramblings:
----
The Windows API is not as efficent as it could be. The sequence of events is at minimum:
* System call to open directory, `CreateFileW`
* System call to read contents, `NtQueryDirectoryFile`
* System call to determine if there's more contents to read, `NtQueryDirectoryFile`
* System call to close directory handle, `CloseHandle`

The most pathological case is 4 system calls to determine there were no contents. The first `NtQueryDirectoryFile`
call always returns that contents were found due to the (unfortunate) "." and ".." entries.

`NtQueryDirectoryFile` also does not have a resulting structure that optimizes our usecase, which is the desire for
just filename and FileAttributes. There's 52 unneeded bytes per entry. Our use case requires at min the next entry
offset, the filename length, and filename -- which would be 8 bytes plus filename per entry, a drastic reduction.

Another issue arrises when passing larger buffers into `NtQueryDirectoryFile`. It becomes a major bottleneck due to what
appears to be an inner call to `ProbeForWrite` touching every page. A buffer growth strategy is used for this reason.
A 10mb constant buffer causes major delays compared to using a small fixed size buffer. On my test case of many very
deep directories but seldome large ones, the constant 10mb buffer took 30 seconds, a constant 4 page buffer took 1.8
seconds. Ideally Windows would only `ProbeForWrite` on used pages, but unfortunately it does not do so.

The ideal would be to write a driver that in the best case does the above 4 system calls in a single system call. In
the worst case, when the buffer was not large enough, returns the handle from `CreateFileW` so that the normal sequence
can commence. I don't know if `ProbeForWrite` would be faster when run strictly on kernel allocated memory.