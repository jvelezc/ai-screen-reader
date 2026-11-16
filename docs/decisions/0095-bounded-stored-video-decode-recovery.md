# 0095: Recover stored-video analysis without repeating website input

A completed task can retain a valid original video while its decoder invocation times out. Treat that as an engine failure, preserve the actual observed goal, and do not repeat the website task. One bounded re-decode of a private snapshot is permitted after the first decoder's exact process and streams have settled. Existing input, capture, byte, frame, pixel and per-invocation time bounds remain.

Record both attempts with safe source-bound diagnostics. Invalid media, cancellation, changed bytes or a second timeout are terminal for that analysis. Missing diagnostics/exit facts remain unknown. The optional diagnostic extension is omitted from historical payloads; existing public constructors and method signatures remain available. No event/video alignment, barrier, website sufficiency or legal conclusion follows from successful decoding. See [validation](../validation/stored-video-decode-recovery-v1.md).
