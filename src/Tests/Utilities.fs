namespace Tests

open System.Reflection
open Aardvark.Base
open Aardium
open Offler
open System
open System.IO
open System.Runtime.InteropServices
open Expecto

[<AutoOpen>]
module Utilities =

    type Aardvark with
        static member InitForTests() =
            IntrospectionProperties.CustomEntryAssembly <- Assembly.GetExecutingAssembly()
            Aardvark.Init()

    module Aardium =

        let initLocal() =
            let folderName =
                if RuntimeInformation.IsOSPlatform OSPlatform.Windows then "Aardium-win32-x64"
                elif RuntimeInformation.IsOSPlatform OSPlatform.Linux then "Aardium-linux-x64"
                elif RuntimeInformation.IsOSPlatform OSPlatform.OSX then
                    match RuntimeInformation.ProcessArchitecture with
                    | Architecture.X64 -> "Aardium-darwin-x64"
                    | Architecture.Arm64 -> "Aardium-darwin-arm64"
                    | arch -> raise <| NotSupportedException $"Unsupported architecture: {arch}"
                else
                    raise <| NotSupportedException $"Unsupported platform: {RuntimeInformation.OSDescription}"

            let path = Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "Aardium", "dist", folderName)
            if not <| Directory.Exists path then
                raise <| DirectoryNotFoundException($"Aardium not found: {path}")

            Aardium.initAt <| Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "Aardium", "dist", folderName)

    module Offler =

        let [<Literal>] width = 640
        let [<Literal>] height = 480

        let start incremental url =
            Aardium.initLocal()

            new Offler {
                url = url
                width = width
                height = height
                incremental = incremental
            }

    module PixImage =

        let tryGetColor (image: PixImage<uint8>) =
            let matrix = image.GetMatrix<C4b>()
            let mutable result = ValueSome matrix.[0, 0]

            for x in 0L .. matrix.Size.X - 1L do
                for y in 0L .. matrix.Size.Y - 1L do
                    if matrix.[x, y] <> matrix.[0, 0] then
                        result <- ValueNone

            result

        let compare (actual: PixImage<'T>) (expected: PixImage<'T>) =
            Expect.equal actual.Size expected.Size "Image size mismatch"

            for x in 0 .. expected.Size.X - 1 do
                for y in 0 .. expected.Size.Y - 1 do
                    for c in 0 .. expected.ChannelCount - 1 do
                        let actualData = actual.GetChannel(int64 c)
                        let expectedData = expected.GetChannel(int64 c)

                        let message =
                            let t = if c < 4 then "color" else "alpha"
                            $"PixImage {t} data mismatch at [{x}, {y}]"

                        Expect.equal actualData.[x, y] expectedData.[x, y] message