#r "nuget: Fake.Core.ReleaseNotes,[5.21.0-alpha004]"
#r "nuget: Fake.Core.Process,[5.21.0-alpha004]"
open System
open System.IO
open System.Diagnostics
open Fake.Core

let exec (name : string) (args : list<string>) =
    let start = ProcessStartInfo(name)
    start.UseShellExecute <- false
    start.CreateNoWindow <- true
    start.RedirectStandardOutput <- true
    start.RedirectStandardError <- true
    for a in args do
        start.ArgumentList.Add a

    let proc = Process.Start(start)
    proc.OutputDataReceived.Add(fun e ->
        if not (isNull e.Data) then
            Trace.trace e.Data
    )
    proc.ErrorDataReceived.Add(fun e ->
        if not (isNull e.Data) then
            Trace.traceError e.Data
    )
    proc.BeginOutputReadLine()
    proc.BeginErrorReadLine()

    proc.WaitForExit()
    if proc.ExitCode <> 0 then
        Trace.traceErrorfn "%s exited with code %d" name proc.ExitCode
        failwithf "%s exited with code %d" name proc.ExitCode



do Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let notes = ReleaseNotes.load "RELEASE_NOTES.md"


File.WriteAllText(Path.Combine("bin, version.txt"), notes.NugetVersion)


Trace.trace "PACKING"

exec "dotnet" [
    "paket"
    "pack"
    "--version"; notes.NugetVersion
    "--release-notes"; String.concat "\r\n" notes.Notes
    "bin/pack"
]


