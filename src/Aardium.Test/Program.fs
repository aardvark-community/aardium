// Learn more about F# at http://fsharp.org

open System
open Aardium


[<EntryPoint>]
let main argv =
    Aardium.init()

    Aardium.run { 
        experimental true
        //size {| P_X = 800; P_Y = 600 |}
        url "https://developer.mozilla.org/en-US/docs/Web/CSS/backdrop-filter#Browser_compatibility"
    }

    0 
