// Learn more about F# at http://fsharp.org

open System
open Aardvark.Base
open Aardium

open System
open System.IO
open System.IO.Pipes
open System.IO.MemoryMappedFiles
open Microsoft.Win32.SafeHandles



[<EntryPoint>]
let main argv =
    Aardium.init()

    let hateThread =
        async {
            do! Async.SwitchToNewThread()
            use pipe = new NamedPipeServerStream("TESTPIPE2", PipeDirection.Out, 1, PipeTransmissionMode.Message, PipeOptions.WriteThrough, 0, 0)
            //use stream = new FileStream(@"\\.\TESTPIPE", FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite, 4096, FileOptions.DeleteOnClose)
            //use w = new StreamWriter(pipe)
    
            pipe.WaitForConnection()

            let data = File.readAllBytes @"C:\Users\Schorsch\Desktop\imgs\P1020962_undistorted.JPG"

            let sw = System.Diagnostics.Stopwatch.StartNew()
            let mutable good = 0;
            while sw.Elapsed.TotalSeconds < 1.0 do  
                pipe.Write(data, 0, data.Length)
                pipe.Flush()
                pipe.WaitForPipeDrain()
                good <- good + 1
            printfn "made: %A (%A per s)" good (float good / sw.Elapsed.TotalSeconds)

        } |> Async.Start
    //     $( document ).ready(function() {
    //    console.log( "ready!" );
    //    var gut = 0;
    //    var pipe = document.aardvark.net.connect("\\\\.\\pipe\\TESTPIPE2", function() { console.warn("connected"); });
    //    pipe.on("data", function(data) { gut++; });
    //});
    //Aardium.run { 
    //    url @"C:\Users\Schorsch\Desktop\oida.html"
    //}
    Aardium.run { 
        url "http://ask.aardvark.graphics"
    }

    0 // return an integer exit code
