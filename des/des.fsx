﻿module des
open System.Collections



module conv =
    // !! damages input !!
    let pad chunks =
        let last = Array.last chunks
        let size = Array.length last
        let difference = 8 - size
        if difference <> 0 then
            let padding = Array.replicate difference (byte difference)
            let padded = Array.append last padding
            Array.set chunks ((Array.length chunks) - 1)  padded
        chunks

    let unpad chunks =
        let lastChunk = Array.last chunks
        let lastByte = Array.last lastChunk
        let padding = (int lastByte)
        let contentLength = 8 - padding
        if padding <= 7 then // maximum padding
            let count = [contentLength + 1  .. chunks.Length]
                         |> List.map (Array.get lastChunk)
                         |> List.forall (fun x -> x = lastByte)
            if count then
                let truncated = Array.take contentLength lastChunk
                Array.set chunks ((Array.length chunks) - 1)  truncated
        chunks
                
                
                


    let toBlocks bytes  =
        bytes 
        |> Array.chunkBySize 8 // 64 bits
        |> pad
        |> Array.map BitArray // BitVector byłby szybszy, ale ma za małą pojemność

    let BitArrayToBytes (bits:BitArray) =
        let ans = Array.replicate 8 0uy
        bits.CopyTo(ans, 0)
        ans
    
    let toBytes blocks =
        blocks
        |> Array.map BitArrayToBytes
        |> unpad
        |> Array.concat


    let split (block:BitArray) =
        let parts = Array.replicate 2 0 // 2 * 32-bit integer
        block.CopyTo(parts, 0) // kopiujemy zawartość do bloków
        let L = BitArray parts[0..0] // i tworzymy na ich podstwie nowe BitArray
        let R = BitArray parts[1..1]
        (L, R)
        
    let join ((L:BitArray), (R:BitArray)) =
        let l = Array.singleton 0
        let r = Array.singleton 0
        L.CopyTo(l, 0)
        R.CopyTo(r, 0)
        let joined = Array.concat [l;r]
        BitArray joined
        
    let ConcatenateFourBitPieces (arr:array<int>) =
        let mutable acc = 0
        for i in arr do
            acc <- ((acc <<< 4) ||| i)        
        BitArray (Array.singleton acc)
        
    let toSixBitPieces (bits:BitArray) =
        let arr = (Array.replicate 48 false)
        bits.CopyTo(arr, 0)
        arr
        |> Array.chunkBySize 6


module crypt =
    module tables =
        //┌───────────────┐
        //│ lookup tables │
        //└───────────────┘
        let initial= [ 57; 49; 41; 33; 25; 17; 9; 1; 59; 51; 43; 35; 27; 19; 11; 3; 61; 53; 45; 37; 29; 21; 13; 5; 63; 55; 47; 39; 31; 23; 15; 7; 56; 48; 40; 32; 24; 16; 8; 0; 58; 50; 42; 34; 26; 18; 10; 2; 60; 52; 44; 36; 28; 20; 12; 4; 62; 54; 46; 38; 30; 22; 14; 6]
        let reverse= [39; 7; 47; 15; 55; 23; 63; 31; 38; 6; 46; 14; 54; 22; 62; 30; 37; 5; 45; 13; 53; 21; 61; 29; 36; 4; 44; 12; 52; 20; 60; 28; 35; 3; 43; 11; 51; 19; 59; 27; 34; 2; 42; 10; 50; 18; 58; 26; 33; 1; 41; 9; 49; 17; 57; 25; 32; 0; 40; 8; 48; 16; 56; 24 ]
        let E= [31; 0; 1; 2; 3; 4; 3; 4; 5; 6; 7; 8; 7; 8; 9; 10; 11; 12; 11; 12; 13; 14; 15; 16; 15; 16; 17; 18; 19; 20; 19; 20; 21; 22; 23; 24; 23; 24; 25; 26; 27; 28; 27; 28; 29; 30; 31; 0; ]
        let P= [15; 6; 19; 20; 28; 11; 27; 16; 0; 14; 22; 25; 4; 17; 30; 9; 1; 7; 23; 13; 31; 26; 2; 8; 18; 12; 29; 5; 21; 10; 3; 24]
        let PC1=[56; 48; 40; 32; 24; 16; 8; 0; 57; 49; 41; 33; 25; 17; 9; 1; 58; 50; 42; 34; 26; 18; 10; 2; 59; 51; 43; 35; 62; 54; 46; 38; 30; 22; 14; 6; 61; 53; 45; 37; 29; 21; 13; 5; 60; 52; 44; 36; 28; 20; 12; 4; 27; 19; 11; 3;]
        let PC2= [13; 16; 10; 23; 0; 4; 2; 27; 14; 5; 20; 9; 22; 18; 11; 3; 25; 7; 15; 6; 26; 19; 12; 1; 40; 51; 30; 36; 46; 54; 29; 39; 50; 44; 32; 47; 43; 48; 38; 55; 33; 52; 45; 41; 49; 35; 28; 31; ]
        let shiftcount = [1;1;2;2;2;2;2;2;1;2;2;2;2;2;2;1;]
            
        // można by tą tablicę zoptymalizować tak, aby uzyskanie adresu było kwestią konkatenacji numeru S (3 bity) i tych 6 bitów adresu (1 tabela, 512 pozycji)
        let Sraw = [[14; 4; 13; 1; 2; 15; 11; 8; 3; 10; 6; 12; 5; 9; 0; 7; 0; 15; 7; 4; 14; 2; 13; 1; 10; 6; 12; 11; 9; 5; 3; 8; 4; 1; 14; 8; 13; 6; 2; 11; 15; 12; 9; 7; 3; 10; 5; 0; 15; 12; 8; 2; 4; 9; 1; 7; 5; 11; 3; 14; 10; 0; 6; 13; ];
                    [ 15; 1; 8; 14; 6; 11; 3; 4; 9; 7; 2; 13; 12; 0; 5; 10; 3; 13; 4; 7; 15; 2; 8; 14; 12; 0; 1; 10; 6; 9; 11; 5; 0; 14; 7; 11; 10; 4; 13; 1; 5; 8; 12; 6; 9; 3; 2; 15; 13; 8; 10; 1; 3; 15; 4; 2; 11; 6; 7; 12; 0; 5; 14; 9];
                    [ 10; 0; 9; 14; 6; 3; 15; 5; 1; 13; 12; 7; 11; 4; 2; 8; 13; 7; 0; 9; 3; 4; 6; 10; 2; 8; 5; 14; 12; 11; 15; 1; 13; 6; 4; 9; 8; 15; 3; 0; 11; 1; 2; 12; 5; 10; 14; 7; 1; 10; 13; 0; 6; 9; 8; 7; 4; 15; 14; 3; 11; 5; 2; 12 ];
                    [ 7; 13; 14; 3; 0; 6; 9; 10; 1; 2; 8; 5; 11; 12; 4; 15; 13; 8; 11; 5; 6; 15; 0; 3; 4; 7; 2; 12; 1; 10; 14; 9; 10; 6; 9; 0; 12; 11; 7; 13; 15; 1; 3; 14; 5; 2; 8; 4; 3; 15; 0; 6; 10; 1; 13; 8; 9; 4; 5; 11; 12; 7; 2; 14; 17]
                    [ 2; 12; 4; 1; 7; 10; 11; 6; 8; 5; 3; 15; 13; 0; 14; 9; 14; 11; 2; 12; 4; 7; 13; 1; 5; 0; 15; 10; 3; 9; 8; 6; 4; 2; 1; 11; 10; 13; 7; 8; 15; 9; 12; 5; 6; 3; 0; 14; 11; 8; 12; 7; 1; 14; 2; 13; 6; 15; 0; 9; 10; 4; 5; 3 ];
                    [ 12; 1; 10; 15; 9; 2; 6; 8; 0; 13; 3; 4; 14; 7; 5; 11; 10; 15; 4; 2; 7; 12; 9; 5; 6; 1; 13; 14; 0; 11; 3; 8; 9; 14; 15; 5; 2; 8; 12; 3; 7; 0; 4; 10; 1; 13; 11; 6; 4; 3; 2; 12; 9; 5; 15; 10; 11; 14; 1; 7; 6; 0; 8; 13 ];
                    [ 4; 11; 2; 14; 15; 0; 8; 13; 3; 12; 9; 7; 5; 10; 6; 1; 13; 0; 11; 7; 4; 9; 1; 10; 14; 3; 5; 12; 2; 15; 8; 6; 1; 4; 11; 13; 12; 3; 7; 14; 10; 15; 6; 8; 0; 5; 9; 2; 6; 11; 13; 8; 1; 4; 10; 7; 9; 5; 0; 15; 14; 2; 3; 12 ];
                    [ 13; 2; 8; 4; 6; 15; 11; 1; 10; 9; 3; 14; 5; 0; 12; 7; 1; 15; 13; 8; 10; 3; 7; 4; 12; 5; 6; 11; 0; 14; 9; 2; 7; 11; 4; 1; 9; 12; 14; 2; 0; 6; 10; 13; 15; 3; 5; 8; 2; 1; 14; 7; 4; 10; 8; 13; 15; 12; 9; 0; 3; 5; 6; 11 ]]


    module permutations =
        let E (bits:BitArray) = // separate from perm for no good reason
            let ans = BitArray (Array.replicate 6 0uy) // 48 bits
            for (loc, old) in (List.indexed tables.E) do
                ans.Set(loc, (bits.Get old))
            ans
        
        let PC1 (key:BitArray) = // 8 -> 7 bytes
            let ans = BitArray (Array.replicate 7 0uy) // 56 bits
            for (loc, old) in (List.indexed tables.PC1) do
                ans.Set(loc, (key.Get old))
            ans
       
        let PC2 (key:BitArray) = // 7 -> 6 bytes
            let ans = BitArray (Array.replicate 6 0uy) // 48 bits
            for (loc, old) in (List.indexed tables.PC2) do
                ans.Set(loc, (key.Get old))
            ans


        let perm locations (bits:BitArray) =
            let ans = BitArray bits
            for (loc, old) in (List.indexed locations) do
                ans.Set(loc, (bits.Get old))
            ans

        let initial = perm tables.initial
        let reverse = perm tables.reverse
        let P = perm tables.P

        let rec keyShift (input:BitArray) count =
            // ABCD EFGH --> BCDA FGHE
            let output = BitArray input
            output.LeftShift(1) |> ignore // ¯\_(ツ)_/¯
            let first = input.Get 0
            let second = input.Get 28
            output.Set(27,first)
            output.Set(55,second)
            match count with
            | 1 -> output
            | _ -> keyShift output (count - 1) 

            
    let expandKey key =
        // key --> subkey lookup table
        let reduced = permutations.PC1 key
        List.scan permutations.keyShift reduced tables.shiftcount
        |> List.map permutations.PC2

    let keySchedule (key:list<BitArray>) n  =
        key.Item n
        

    let S (n, addr) =
        tables.Sraw[n][addr]
    
    let makeSAddress bits =
        let unpacked = bits |> Array.map (fun b -> if b then 1 else 0)
        let i = unpacked[0] * 2 + unpacked[5]
        let j = unpacked[1] * 8 + unpacked[2] * 4 + unpacked[3] * 2 + unpacked[4]
        i * 16 + j
        
    

    
    let cipher (keyPart:BitArray) (bits:BitArray) = // the $f$ function
        let parts = (permutations.E bits).Xor keyPart
        let output = BitArray (Array.singleton 0)
        parts
        |> conv.toSixBitPieces
        |> Array.map makeSAddress
        |> Array.indexed
        |> Array.map S
        |> conv.ConcatenateFourBitPieces
        |> permutations.P
        // BitArray bits // NSA: (╹◡╹)

    let rec cryptIter key n ((L:BitArray), (R:BitArray)) =
        printf "iteracja %i" n
        let L' = R
        let keyPart = keySchedule key n
        let R' = (BitArray L).Xor (cipher keyPart R)
        match n with // List.fold?
            |16 -> (R', L')
            |_ -> cryptIter key (n + 1) (L', R')
        
        
        
    let cryptBlock key block  =
        block
        |> conv.split 
        |> cryptIter key 1 
        |> conv.join
        


    let encryptBlock key block =
        let keyList = expandKey key
        block
        |> permutations.initial
        |> cryptBlock keyList
        |> permutations.reverse
        

module debug =

    let bools2hex bin =
        bin
        |> Array.map (fun i -> if i then 1 else 0)
        |> Array.reduce (fun a b -> (a * 2) + b)
        |> sprintf "%x"


    let hex2bools str =
        let num =
            System.Int32.Parse(str, System.Globalization.NumberStyles.AllowHexSpecifier)
        [| 3..-1..0 |]
        |> Array.map (fun i -> (num >>> i) % 2 = 1)

    
    let toStr (bits:BitArray) =
        let inter = (Array.replicate bits.Length false)
        bits.CopyTo(inter, 0)
        inter |> Array.chunkBySize 4 |> Array.map bools2hex
