#load @"paket-files/build/aardvark-platform/aardvark.fake/DefaultSetup.fsx"

open Fake
open System
open System.IO
open System.Diagnostics
open Aardvark.Fake

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
        match Fake.ProcessHelper.tryFindFileOnPath yarnName with
            | Some path -> path
            | None -> failwith "could not locate yarn"

    let ret = 
        Fake.ProcessHelper.ExecProcess (fun info ->
            info.FileName <- yarn
            info.WorkingDirectory <- "Aardium"
            info.Arguments <- String.concat " " args
            ()
        ) TimeSpan.MaxValue

    if ret <> 0 then
        failwith "yarn failed"


Target "InstallYarn" (fun () ->

    match Fake.ProcessHelper.tryFindFileOnPath yarnName with
        | None ->
    
            match Fake.ProcessHelper.tryFindFileOnPath npmName with
                | Some npm ->
                    
                    let ret = 
                        Fake.ProcessHelper.ExecProcess (fun info ->
                            info.FileName <- npm
                            info.Arguments <- "install -g yarn"
                            ()
                        ) TimeSpan.MaxValue

                    if ret <> 0 then
                        failwith "npm install failed"
                | None ->
                    failwith "could not locate npm"   
        | _ ->
            tracefn "yarn already installed"
)

Target "Yarn" (fun () ->
    yarn []
)

Target "YarnPack" (fun () ->
    yarn ["dist"]
)

"InstallYarn" ==> "Yarn" ==> "YarnPack" ==> "CreatePackage"

entry()