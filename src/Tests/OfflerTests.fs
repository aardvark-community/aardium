namespace Tests

open System
open System.IO
open System.Threading
open Aardvark.Base
open Expecto

module Offler =

    module Cases =
        let private htmlPath = Path.Combine(__SOURCE_DIRECTORY__, "html")

        let unknownMessages() =
            use offler = Offler.start false "about:blank"

            let messages = ResizeArray<string>()
            offler.UnknownMessageReceived.Add (fun message ->
                lock messages (fun _ ->
                    messages.Add message
                    Monitor.Pulse messages
                )
            )

            offler.StartJavascript "socket.send('foo');"
            offler.StartJavascript "socket.send('bar');"

            lock messages (fun _ ->
                let isDone() = messages.Contains "foo" && messages.Contains "bar"
                while not <| isDone() do
                    let success = Monitor.Wait(messages, TimeSpan.FromSeconds 10)
                    if not success then
                        failtest $"Timeout, messages: {List.ofSeq messages}"

                Expect.containsAll messages ["foo"; "bar"] "Missing messages"
            )

        let staticPage() =
            Aardvark.InitForTests()

            let uri = Uri(Path.Combine(htmlPath, "static.html"))
            use offler = Offler.start true uri.AbsoluteUri

            let lockObj = obj()
            let mutable images = 0

            offler.Add (fun _ ->
                lock lockObj (fun _ ->
                    &images += 1
                    Monitor.Pulse lockObj
                )
            )

            lock lockObj (fun _ ->
                while Monitor.Wait(lockObj, TimeSpan.FromSeconds 10) do ()

                Expect.isGreaterThan images 0 "No images retrieved"

                let expected = PixImage.Load(Path.Combine(htmlPath, "checker.png")).AsPixImage<uint8>()
                let actual = offler.LastImage

                PixImage.compare actual expected
            )

        let dynamicPage (incremental: bool) () =
            let uri = Uri(Path.Combine(htmlPath, "dynamic.html"))
            use offler = Offler.start incremental uri.AbsoluteUri

            let mutable images = ResizeArray()

            offler.Add (fun info ->
                if not incremental then
                    Expect.isTrue info.isFullFrame "Expected full frame"

                lock images (fun _ ->
                    images.Add offler.LastImage
                    Monitor.Pulse images
                )
            )

            let expectedColors = [
                C4b(255, 0, 0)
                C4b(0, 255, 0)
                C4b(0, 0, 255)
            ]

            lock images (fun _ ->
                while images.Count < 10 && Monitor.Wait(images, TimeSpan.FromSeconds 10) do ()
                Expect.isGreaterThanOrEqual images.Count 10 "Not enough images retrieved"

                let mutable valid = 0

                for i = 0 to images.Count - 1 do
                    let image = images.[i]

                    if image.Size = V2i(Offler.width, Offler.height) then
                        let subimage = image.SubImage(V2i.Zero, V2i(32, 32))

                        match PixImage.tryGetColor subimage with
                        | ValueSome color when expectedColors |> List.contains color ->
                            &valid += 1

                        | _ when valid > 0 ->
                            failtest $"Image {i} is not valid, but {valid} images have already been received"

                        | _ -> ()

                Expect.isGreaterThan valid 0 "No valid images received"
            )

    [<Tests>]
    let tests =
        testList "Offler" [
            testCase "Unknown messages"           Cases.unknownMessages
            testCase "Static page"                Cases.staticPage
            testCase "Dynamic page"               (Cases.dynamicPage false)
            testCase "Dynamic page (incremental)" (Cases.dynamicPage true)
        ]