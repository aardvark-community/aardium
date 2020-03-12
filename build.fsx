#r "paket: groupref Build //"
#load ".fake/build.fsx/intellisense.fsx"
#load @"paket-files/build/aardvark-platform/aardvark.fake/DefaultSetup.fsx"

open System
open System.IO
open System.Diagnostics
open System.Runtime.InteropServices
open Aardvark.Fake
open Fake.Core
open Fake.Core.TargetOperators

do Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

DefaultSetup.install ["src/Aardium.sln"]

let yarnName =
    if Environment.OSVersion.Platform = PlatformID.Unix || Environment.OSVersion.Platform = PlatformID.MacOSX then "yarn"
    else "yarn.cmd"

let npmName =
    if Environment.OSVersion.Platform = PlatformID.Unix || Environment.OSVersion.Platform = PlatformID.MacOSX then "npm"
    else "npm.cmd"

let yarn (args : list<string>) =
    let yarn =
        match ProcessUtils.tryFindFileOnPath yarnName with
            | Some path -> path
            | None -> failwith "could not locate yarn"

    let ret : ProcessResult<_> = 
        Command.RawCommand(yarn, Arguments.ofList args)
        |> CreateProcess.fromCommand
        |> CreateProcess.withWorkingDirectory "Aardium"
        |> Proc.run
        //ProcessHelper.ExecProcess (fun info ->
        //     info.FileName <- yarn
        //     info.WorkingDirectory <- "Aardium"
        //     info.Arguments <- String.concat " " args
        //     ()
        // ) TimeSpan.MaxValue

    if ret.ExitCode <> 0 then
        failwith "yarn failed"


Target.create "InstallYarn" (fun _ ->

    match ProcessUtils.tryFindFileOnPath yarnName with
        | None ->
    
            match ProcessUtils.tryFindFileOnPath npmName with
                | Some npm ->
                    
                    let ret = 
                        Command.RawCommand(npm, Arguments.ofList ["install -g yarn"])
                        |> CreateProcess.fromCommand
                        |> Proc.run

                    if ret.ExitCode <> 0 then
                        failwith "npm install failed"
                | None ->
                    failwith "could not locate npm"   
        | _ ->
            Trace.tracefn "yarn already installed"
)

Target.create "Yarn" (fun _ ->
    yarn []
)

Target.create "YarnPack" (fun _ ->
    if RuntimeInformation.IsOSPlatform OSPlatform.Windows then 
        yarn ["dist"]
        File.WriteAllBytes("Aardium/dist/Aardium-Linux-x64.tar.gz", [||]) |> ignore
        File.WriteAllBytes("Aardium/dist/Aardium-Darwin-x64.tar.gz", [||]) |> ignore
    if RuntimeInformation.IsOSPlatform OSPlatform.Linux then 
        yarn ["dist"]
        Directory.CreateDirectory "Aardium/dist/Aardium-win32-x64" |> ignore
        File.WriteAllBytes("Aardium/dist/Aardium-Darwin-x64.tar.gz", [||]) |> ignore
    if RuntimeInformation.IsOSPlatform OSPlatform.OSX then 
        yarn ["dist"]
        File.WriteAllBytes("Aardium/dist/Aardium-Linux-x64.tar.gz", [||]) |> ignore
        Directory.CreateDirectory "Aardium/dist/Aardium-win32-x64" |> ignore
)

"InstallYarn" ==> "Yarn" ==> "YarnPack" ==> "CreatePackage"

entry()