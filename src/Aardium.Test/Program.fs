// Learn more about F# at http://fsharp.org

open System
open System.IO
open System.Runtime.InteropServices
open Aardium
open Offler
open Aardvark.Base


[<EntryPoint>]
let main argv =
    Aardvark.Init()
    
    // local aardium
    let distPath = 
        Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "Aardium", "dist")
    let distName =
        if RuntimeInformation.IsOSPlatform OSPlatform.Windows then "Aardium-win32-x64"
        elif RuntimeInformation.IsOSPlatform OSPlatform.Linux then "Aardium-linux-x64"
        elif RuntimeInformation.IsOSPlatform OSPlatform.OSX then "Aardium-darwin-x64"
        else failwith "bad platform"

    let exe = Path.Combine(distPath, distName)
    Aardium.initPath exe
    //Aardium.init()

    let offler = true

    if offler then
        Offler.Logger <- fun _ msg ->
            printfn "[Offler] %s" msg

        let offler = 
            new Offler {
                url = "https://www.w3schools.com/css/css3_animations.asp"
                width = 1920
                height = 1080
                incremental = true
            }

        let outputPath =
            Path.combine [
                Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop)
                "offler"
            ]

        if not <| Directory.Exists outputPath then
            Directory.CreateDirectory outputPath |> ignore

        let mutable index = 0
        offler.Add(fun info ->
            printfn "image %03d" index
            offler.LastImage.Save (Path.Combine(outputPath, $"%03d{index}.png"))
            index <- index + 1
        )

        offler.UnknownMessageReceived.Add(fun msg ->
            Log.warn "got: %A" msg
        )
        offler.StartJavascript "socket.send(\"bla\");"
        Console.ReadLine() |> ignore

        offler.Resize(1024, 1024)
        Console.ReadLine() |> ignore

        offler.Dispose()

    else 
        Aardium.run { 
            experimental true
            //size {| P_X = 800; P_Y = 600 |}
            url "https://developer.mozilla.org/en-US/docs/Web/CSS/backdrop-filter#Browser_compatibility"
            //windowoptions {| titleBarStyle = "customButtonsOnHover"; frame= false |}
            title "test"
            autoclose true // close when mainwindow closes
            hideDock false // hide dock on mac
            log (fun msg -> Report.Line(2, $"[Aardium] %s{msg}"))
        }

    0 