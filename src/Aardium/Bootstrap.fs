namespace Aardium

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Threading
open System.Diagnostics
open System.Runtime.InteropServices
open Microsoft.FSharp.Reflection

[<AutoOpen>]
module private Utilities =

    let failf fmt =
        Printf.kprintf (fun str ->
            failwithf "[Aardium] %s" str
        ) fmt

    module Log =
        let line fmt =
            Printf.kprintf (fun str ->
                printfn "[Aardium] %s" str
            ) fmt

    module Directory =

        let isWritable path =
            try
                let file = Path.Combine(path, Path.GetRandomFileName())
                use _ = File.Create(file, 0, FileOptions.DeleteOnClose)
                true
            with _ ->
                false

        let create (ensureWritable : bool) (path : string) =
            if not <| Directory.Exists path then
                Directory.CreateDirectory path |> ignore

            if ensureWritable && not <| isWritable path then
                raise <| UnauthorizedAccessException($"Cannot create files in '{path}'.")

module private Tools =

    [<Struct>]
    type Progress(received : int64, total : int64) =
        member x.Received = received
        member x.Total = total
        member x.Relative = float received / float total

        override x.ToString() =
            sprintf "%.2f%%" (100.0 * float received / float total)

    let unzip (file : string) (folder : string) =
        try
            if Directory.Exists folder then Directory.Delete(folder, true)
            ZipFile.ExtractToDirectory(file, folder)

        with _ ->
            if Directory.Exists folder then Directory.Delete(folder, true)
            reraise()

    let untar (file : string) (folder : string) =
        let args = $"-zxvf \"%s{file}\" -C \"%s{folder}\""
        Log.line "tar %s" args

        use p = new Process()
        p.StartInfo.FileName <- "tar"
        p.StartInfo.Arguments <- args
        p.StartInfo.RedirectStandardOutput <- true
        p.StartInfo.RedirectStandardError <- true
        p.StartInfo.UseShellExecute <- false
        p.StartInfo.CreateNoWindow <- true

        let output = ResizeArray<string>()

        p.OutputDataReceived.Add (fun args ->
            if not <| String.IsNullOrWhiteSpace args.Data then
                lock output (fun _ -> output.Add args.Data)
        )
        p.ErrorDataReceived.Add ignore

        p.Start() |> ignore
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            let output = output |> String.concat Environment.NewLine
            let info = if String.IsNullOrEmpty output then "" else ": " + output
            failf "Failed to untar '%s'%s" file info

    let download (progress : Progress -> unit) (url : string) (file : string) =
        try
            use c = new HttpClient()

            let response = c.GetAsync(System.Uri url, HttpCompletionOption.ResponseHeadersRead).Result
            let len =
                let f = response.Content.Headers.ContentLength
                if f.HasValue then f.Value
                else 1L <<< 30

            if not response.IsSuccessStatusCode then
                let code = response.StatusCode
                raise <| HttpRequestException($"Http GET request failed with status code {int code} ({code}).")

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
                if sw.Elapsed.TotalSeconds >= 0.1 && p.Relative - lastProgress.Relative > 0.1 then
                    progress p
                    lastProgress <- p
                    sw.Restart()

        with _ ->
            if File.Exists file then File.Delete file
            reraise()

module private ElectronProcess =

    let private startThread (f : unit -> unit) =
        let t = Thread(ThreadStart(f), IsBackground = true)
        t.Start()

    let start (file : string) (logger : bool -> string -> unit) (args : string[]) =
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
        FSys.Process.attachChild proc

        proc.OutputDataReceived.Add (fun e ->
            if not (String.IsNullOrWhiteSpace e.Data) then
                logger false e.Data
        )

        proc.ErrorDataReceived.Add (fun e ->
            if not (String.IsNullOrWhiteSpace e.Data) then
                logger true e.Data
        )

        proc.BeginOutputReadLine()
        proc.BeginErrorReadLine()

        proc

    let exec (file : string) (logger : bool -> string -> unit) (args : string[]) =
        use log = new System.Collections.Concurrent.BlockingCollection<string * bool>()

        use proc =
            args |> start file (fun error str ->
                log.Add((str, error))
            )

        let cancel = new CancellationTokenSource()

        startThread (fun () ->
            proc.WaitForExit()
            cancel.Cancel()
        )

        try
            while true do
                let (str, error) = log.Take(cancel.Token)
                logger error str

        with :? OperationCanceledException ->
            ()

module private Strings =

    module Platform =

        [<Literal>]
        let Win = "Win32"

        [<Literal>]
        let Linux = "Linux"

        [<Literal>]
        let Darwin = "Darwin"

    let version =
        let asm = typeof<Tools.Progress>.Assembly

        asm.GetCustomAttributes(true)
        |> Array.tryPick (function
            | :? System.Reflection.AssemblyInformationalVersionAttribute as att -> Some att.InformationalVersion
            | _ -> None
        )
        |> Option.defaultWith (fun _ ->
            string <| asm.GetName().Version
        )

    let platform =
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then Platform.Win
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then Platform.Linux
        elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then Platform.Darwin
        else "Unknown"

    let architecture =
        match RuntimeInformation.ProcessArchitecture with
        | Architecture.X64 -> "x64"
        | Architecture.Arm64 -> "arm64"
        | arch ->
            failf $"Unsupported architecture '{arch}'."

    let packageName = $"Aardium-%s{platform}-%s{architecture}"

    let packageUrl =
        $"https://www.nuget.org/api/v2/package/%s{packageName}/%s{version}"

    let binaryName =
        match platform with
        | Platform.Win    -> "Aardium.exe"
        | Platform.Linux  -> "Aardium"
        | Platform.Darwin -> "Aardium.app/Contents/MacOS/Aardium"
        | platform ->
            failf $"Unsupported platform '{platform}'."

    let binaryPaths = [|
        binaryName
        Path.Combine(architecture, version, binaryName)
        Path.Combine(architecture, version, "tools", binaryName)
    |]

    // Use local AppData like Aardvark
    // On Windows we don't want to put > 100MB into the Roaming folder.
    let defaultCachePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aardium")

type AardiumConfig =
    {
        width        : Option<int>
        height       : Option<int>
        url          : Option<string>
        debug        : bool
        icon         : Option<string>
        title        : Option<string>
        menu         : bool
        fullscreen   : bool
        hideDock     : bool
        autoclose    : bool
        experimental : bool
        woptions     : Option<string>
        log          : bool -> string -> unit
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
            hideDock = false
            autoclose = false
            woptions = None
            log = fun _isError _ln -> ()
        }

    let internal toArgs (cfg : AardiumConfig) =
        [|
            match cfg.debug with
            | true -> yield "--dev"
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

            match cfg.hideDock with
            | true -> yield! [| "--hideDock"|]
            | false -> ()

            match cfg.autoclose with
            | true -> yield! [| "--autoclose"|]
            | false -> ()

            match cfg.woptions with
            | Some w -> yield "--woptions=\""  + w.Replace("\"", "\\\"") + "\""
            | None -> ()
        |]

type AardiumOffscreenServer internal (proc : Process, port : int) =
    let mutable disposed = false

    member x.IsRunning = not disposed && not proc.HasExited
    member x.Port = port

    member x.Stop() =
        if x.IsRunning then proc.Kill()

    member x.Dispose() =
        x.Stop()
        if not disposed then
            proc.Dispose()
            disposed <- true

    interface System.IDisposable with
        member x.Dispose() = x.Dispose()

module Aardium =
    let mutable private binaryPath = ""

    /// Returns whether Aardium has been successfully initialized.
    let isInitialized() =
        not (String.IsNullOrWhiteSpace binaryPath) && File.Exists binaryPath

    let private findBinary (path : string) =
        Strings.binaryPaths |> Array.tryPick (fun p ->
            let p = Path.Combine(path, p)
            if File.Exists p then Some p else None
        )
        |> Option.defaultValue ""

    /// Initializes Aardium in the given directory.
    /// If the binary cannot be found, it is retrieved from nuget.org.
    let initAt (path : string) =
        if not <| isInitialized() then
            let mutable path = path

            if String.IsNullOrWhiteSpace path then
                path <- Strings.defaultCachePath

            path <- Path.GetFullPath path
            binaryPath <- findBinary path

            if not <| isInitialized() then
                // Ensure we have a directory to work in (i.e. write access)
                try
                    Directory.create true path
                with _ ->
                    if path <> Strings.defaultCachePath then
                        Log.line "Failed to create or use directory '%s' with write access, falling back to '%s'" path Strings.defaultCachePath
                        path <- Strings.defaultCachePath
                        Directory.create true path
                    else
                        failf "Failed to create or use directory '%s' with write access." path

                // Download nupkg
                let nupkgPath =
                    Path.Combine(path, $"%s{Strings.packageName}-%s{Strings.version}.nupkg")

                Log.line "Downloading from %s to '%s'" Strings.packageUrl nupkgPath

                (Strings.packageUrl, nupkgPath) ||> Tools.download (fun p ->
                    Log.line "Downloading %.0f%%" (p.Relative * 100.0)
                )

                Log.line "Download complete, extracting..."

                // Extract (for non-Windows we have to extract the contained tar.gz as well)
                let finalPath = Path.Combine(path, Strings.architecture, Strings.version)

                try
                    Directory.create false finalPath
                    Tools.unzip nupkgPath finalPath

                    binaryPath <- findBinary path

                    match Strings.platform with
                    | Strings.Platform.Linux | Strings.Platform.Darwin when not <| isInitialized() ->
                        let toolsPath = Path.Combine(finalPath, "tools")
                        let tarPath = Path.Combine(toolsPath, $"Aardium-%s{Strings.platform}-%s{Strings.architecture}.tar.gz")

                        if File.Exists tarPath then
                            Tools.untar tarPath toolsPath
                            try File.Delete tarPath
                            with _ -> ()
                    | _ ->
                        ()

                    if not <| isInitialized() then
                        failf "Could not find binary after extracting to '%s'." finalPath

                finally
                    try File.Delete nupkgPath
                    with _ -> ()

        Log.line "Initialized at '%s'" binaryPath

    /// Initializes Aardium in the default cache location.
    /// If the binary cannot be found, it is retrieved from nuget.org.
    /// The default location is AppData/Local/Aardium on Windows and ~/.local/share/Aardium on Linux / MacOS.
    let init() =
        initAt null

    [<Obsolete("Use Aardium.initAt instead.")>]
    let initPath (path : string) =
        initAt path

    let runConfig (cfg : AardiumConfig)  =
        if isInitialized() then
            ElectronProcess.exec binaryPath cfg.log (AardiumConfig.toArgs cfg)
        else
            raise <| InvalidOperationException("Aardium has not been initialized.")

    let startOffscreenServer (port : int) (logger : bool -> string -> unit) =
        let port =
            if port <> 0 then
                port
            else
                let l = System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)
                l.Start()
                let p = l.LocalEndpoint |> unbox<System.Net.IPEndPoint>
                let port = p.Port
                l.Stop()
                port

        if isInitialized() then
            logger false (sprintf "starting server with port %d" port)
            let proc = ElectronProcess.start binaryPath logger [|sprintf "--server=%d" port|]
            new AardiumOffscreenServer(proc, port)
        else
            raise <| InvalidOperationException("Aardium has not been initialized.")

    type AardiumBuilder() =
        member x.Yield(()) = AardiumConfig.empty

        [<CustomOperation("windowoptions")>]
        member x.WindowOptions(cfg : AardiumConfig, value : 'a) =
            if FSharpType.IsRecord(typeof<'a>, true) then
                let rec toJSON(t : Type) (value : obj) =
                    if t = typeof<string> then
                        sprintf "\"%s\"" (unbox<string> value)
                    elif t = typeof<bool> then
                        if unbox value then "true" else "false"
                    elif t = typeof<float> || t = typeof<int> then
                        string value
                    elif FSharpType.IsRecord(t, true) then
                        FSharpType.GetRecordFields(typeof<'a>, true)
                        |> Array.map (fun f ->
                            let value = toJSON f.PropertyType (f.GetValue value)
                            sprintf "\"%s\": %s" f.Name value
                        )
                        |> String.concat ", "
                        |> sprintf "{ %s }"
                    else
                        let seq = t.GetInterface(typedefof<seq<_>>.FullName)
                        if isNull seq then failf "Unknown type %A" t
                        else
                            let e = (value :?> System.Collections.IEnumerable).GetEnumerator()
                            let t = seq.GetGenericArguments().[0]
                            let all = System.Collections.Generic.List<obj>()
                            while e.MoveNext() do
                                all.Add e.Current
                            all |> Seq.map (toJSON t) |> String.concat ", " |> sprintf "[%s]"

                let json = toJSON typeof<'a> value
                { cfg with woptions = Some json }
            else
                failf "Bad window options."

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

        [<CustomOperation("hideDock")>]
        member x.HideDock(cfg : AardiumConfig, v : bool) =
            { cfg with hideDock = v }

        [<CustomOperation("autoclose")>]
        member x.AutoClose(cfg : AardiumConfig, v : bool) =
            { cfg with autoclose = v }

        [<CustomOperation("menu")>]
        member x.Menu(cfg : AardiumConfig, v : bool) =
            { cfg with menu = v }

        [<CustomOperation("experimental")>]
        member x.Experimental(cfg : AardiumConfig, v : bool) =
            { cfg with experimental = v }

        [<CustomOperation("log")>]
        member x.Log(cfg : AardiumConfig, log : bool -> string -> unit) =
            { cfg with log = log }

        [<CustomOperation("log")>]
        member x.Log(cfg : AardiumConfig, log : string -> unit) =
            { cfg with log = fun _ -> log }

        member x.Run(cfg : AardiumConfig) =
            runConfig cfg

    let run = AardiumBuilder()