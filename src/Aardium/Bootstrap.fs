namespace Aardium

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open System.Diagnostics


[<Struct>]
type Progress(received : int64, total : int64) =
    member x.Received = received
    member x.Total = total
    member x.Relative = float received / float total

    override x.ToString() =
        sprintf "%.2f%%" (100.0 * float received / float total)

module Tools =
    
    type private Message =
        | Log of string
        | Error of string

    let unzip (file : string) (folder : string) =
        try
            if Directory.Exists folder then Directory.Delete(folder, true)
            ZipFile.ExtractToDirectory(file, folder)
        with _ ->
            if Directory.Exists folder then Directory.Delete(folder, true)
            reraise()

    let download (progress : Progress -> unit) (url : string) (file : string) =
        try
            use c = new HttpClient()

            let response = c.GetAsync(System.Uri url, HttpCompletionOption.ResponseHeadersRead).Result
            let len = 
                let f = response.Content.Headers.ContentLength
                if f.HasValue then f.Value
                else 1L <<< 30
                
            let mutable lastProgress = Progress(0L,len)
            progress lastProgress
            let sw = System.Diagnostics.Stopwatch.StartNew()


            use stream = response.Content.ReadAsStreamAsync().Result
            if File.Exists file then File.Delete file
            use output = File.OpenWrite(file)


            let buffer : byte[] = Array.zeroCreate (4 <<< 20)
            
            let mutable remaining = len
            let mutable read = 0L
            while remaining > 0L do
                let cnt = int (min remaining buffer.LongLength)
                let r = stream.Read(buffer, 0, cnt)
                output.Write(buffer, 0, r)
            
                remaining <- remaining - int64 r
                read <- read + int64 r


                let p = Progress(read, len)
                if sw.Elapsed.TotalSeconds >= 0.1 || p.Relative - lastProgress.Relative > 0.05 then
                    progress p
                    lastProgress <- p
                    sw.Restart()
                    
        with _ ->
            if File.Exists file then File.Delete file
            reraise()
          
    let startThread (f : unit -> unit) =
        let t = new Thread(ThreadStart(f), IsBackground = true)
        t.Start()

    let exec (file : string) (logger : bool -> string -> unit) (args : string[]) =
        let info = 
            ProcessStartInfo(
                file, 
                Arguments = String.concat " " args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            )
            
        info.EnvironmentVariables.["ELECTRON_ENABLE_LOGGING"] <- "1"
        info.Environment.["ELECTRON_ENABLE_LOGGING"] <- "1"

        let proc = Process.Start(info)
        use log = new System.Collections.Concurrent.BlockingCollection<Message>()

        proc.OutputDataReceived.Add (fun e ->
            if not (String.IsNullOrWhiteSpace e.Data) then
                log.Add (Log e.Data)
        )
        
        proc.ErrorDataReceived.Add (fun e ->
            if not (String.IsNullOrWhiteSpace e.Data) then
                log.Add (Error e.Data)
        )
        
        proc.BeginOutputReadLine()
        proc.BeginErrorReadLine()
        
        let cancel = new CancellationTokenSource()

        startThread (fun () ->
            proc.WaitForExit()
            cancel.Cancel()
        )
        
        try
            while true do
                let msg = log.Take(cancel.Token)
                match msg with
                    | Log msg -> logger false msg
                    | Error msg -> logger true msg
        with :? OperationCanceledException ->
            ()


type AardiumConfig =
    {
        width       : Option<int>
        height      : Option<int>
        url         : Option<string>
        debug       : bool
        icon        : Option<string>
        title       : Option<string>
        menu        : bool
        fullscreen  : bool
        experimental: bool
        log         : bool -> string -> unit
    }

module AardiumConfig =
    let empty = 
        {
            width = None
            height = None
            url = None
            debug = true
            icon = None
            title = None
            menu = false
            fullscreen = false
            experimental = false
            log = fun isError ln -> ()
        }

    let internal toArgs (cfg : AardiumConfig) =
        [|
        
            match cfg.debug with
                | true -> yield "--debug"
                | false -> ()

            match cfg.menu with
                | true -> yield "--menu"
                | false -> ()

            match cfg.fullscreen with
                | true -> yield "--fullscreen"
                | false -> ()

            match cfg.experimental with
                | true -> yield "--experimental"
                | false -> ()

            match cfg.width with
                | Some w -> yield! [| "--width=" + string w |]
                | None -> ()

            match cfg.height with
                | Some h -> yield! [| "--height=" + string h |]
                | None -> ()

            match cfg.url with
                | Some url -> yield! [| "--url=\"" + url + "\"" |]
                | None -> ()

            
            match cfg.title with    
                | Some t -> yield! [| "--title=\"" + t + "\"" |]
                | None -> ()
                
            match cfg.icon with    
                | Some i -> yield! [| "--icon=\"" + i + "\"" |]
                | None -> ()
                

        |]


module Aardium =
    open System.Runtime.InteropServices

    let feed = "https://www.nuget.org/api/v2/package"
    let packageBaseName = "Aardium"
    let version = "1.0.26"

    [<Literal>]
    let private Win = "Win32"
    [<Literal>]
    let private Linux = "Linux"
    [<Literal>]
    let private Darwin = "Darwin"

    let private platform =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then Win
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then Linux
        elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then Darwin
        else failwith "unsupported platform"

    let private arch =
        match sizeof<nativeint> with
            | 4 -> "x86"
            | 8 -> "x64"
            | v -> failwithf "bad bitness: %A" v

    let private packageName = sprintf "%s-%s-%s" packageBaseName platform arch

    let private exeName =
        match platform with
            | Win -> "Aardium.exe"
            | Linux -> "Aardium"
            | Darwin -> "Aardium.app"
            | _ -> failwith "unsporrted platform"

    //let private cachePath =
    //    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aardium")

    let mutable private cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aardium")

    module Libc =
        open System.Runtime.InteropServices
        [<DllImport("libc")>]
        extern int chmod(string path, int mode)
        
    let initPath (path : string) =
        cachePath <- path //Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aardium")
        let info = DirectoryInfo cachePath
        if not info.Exists then info.Create()

        let aardiumPath = Path.Combine(cachePath, arch, version)
        let info = DirectoryInfo aardiumPath

        if not info.Exists || Directory.GetFiles(aardiumPath).Length = 0 then
            info.Create()

            let fileName = sprintf "%s.%s.nupkg" packageName version
            let tempFile = Path.Combine(cachePath, arch, fileName)
            let url = sprintf "%s/%s/%s" feed packageName version

            Console.Write("downloading")
            Tools.download ignore  url tempFile
            Console.WriteLine("done.")

            Tools.unzip tempFile aardiumPath
            match platform with
                | Linux | Darwin -> 
                    let worked = Libc.chmod(Path.Combine(aardiumPath, "tools", "Aardium"), 0b111101101)
                    if worked <> 0 then printfn "chmod failed. consider to chmod +x Aardium"
                | _ -> ()

    let init() =
        initPath (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aardium"))

    let runConfig (cfg : AardiumConfig)  =
        let aardiumPath = Path.Combine(cachePath, arch, version, "tools", exeName)
        if File.Exists aardiumPath then
            Tools.exec aardiumPath cfg.log (AardiumConfig.toArgs cfg) 
        else
            failwithf "could not locate aardium"


    type AardiumBuilder() =
        member x.Yield(()) = AardiumConfig.empty

        [<CustomOperation("url")>]
        member x.Url(cfg : AardiumConfig, url : string) =
            { cfg with url = Some url }
            
        [<CustomOperation("icon")>]
        member x.Icon(cfg : AardiumConfig, file : string) =
            { cfg with icon = Some file }
            
        [<CustomOperation("title")>]
        member x.Title(cfg : AardiumConfig, title : string) =
            { cfg with title = Some title }

        [<CustomOperation("width")>]
        member x.Width(cfg : AardiumConfig, w : int) =
            { cfg with width = Some w }
            
        [<CustomOperation("height")>]
        member x.Height(cfg : AardiumConfig, h : int) =
            { cfg with height = Some h }
            
        [<CustomOperation("size")>]
        member inline x.Size(cfg : AardiumConfig, v : ^a) =
            let w = (^a: (member P_X : int)(v))
            let h = (^a: (member P_Y : int)(v))
            { cfg with width = Some w; height = Some h }
            
        [<CustomOperation("debug")>]
        member x.Debug(cfg : AardiumConfig, v : bool) =
            { cfg with debug = v }

        [<CustomOperation("fullscreen")>]
        member x.Fullscreen(cfg : AardiumConfig, v : bool) =
            { cfg with fullscreen = v }
            
        [<CustomOperation("menu")>]
        member x.Menu(cfg : AardiumConfig, v : bool) =
            { cfg with menu = v }
            
        [<CustomOperation("experimental")>]
        member x.Experimental(cfg : AardiumConfig, v : bool) =
            { cfg with experimental = v }

        member x.Run(cfg : AardiumConfig) =
            runConfig cfg


    let run = AardiumBuilder()
        
