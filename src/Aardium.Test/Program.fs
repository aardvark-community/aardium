﻿// Learn more about F# at http://fsharp.org

open System
open System.IO
open System.Runtime.InteropServices
open Aardium


[<EntryPoint>]
let main argv =
    let distPath = 
        Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "Aardium", "dist")
    let distName =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then "Aardium-win32-x64"
        elif RuntimeInformation.IsOSPlatform OSPlatform.Linux then "Aardium-linux-x64"
        elif RuntimeInformation.IsOSPlatform OSPlatform.OSX then "Aardium-darwin-x64"
        else failwith "bad platform"

    let exe = Path.Combine(distPath, distName)
    Aardium.initPath exe

    let ico = Path.Combine(exe,"resources","app","aardvark.ico")

    Aardium.run { 
        experimental true
        //size {| P_X = 800; P_Y = 600 |}
        url "https://developer.mozilla.org/en-US/docs/Web/CSS/backdrop-filter#Browser_compatibility"
        //windowoptions {| titleBarStyle = "customButtonsOnHover"; frame= false |}
        title "gugu"
        icon ico
        autoclose true // close when mainwindow closes
        hideDock true // hide dock on mac
    }

    0 
