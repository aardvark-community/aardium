// Learn more about F# at http://fsharp.org

open System
open Aardvark.Base
open Aardium


[<EntryPoint>]
let main argv =
    Aardium.init()

    Aardium.run { 
        experimental true
        url "https://developer.mozilla.org/en-US/docs/Web/CSS/backdrop-filter#Browser_compatibility"
    }

    0 
