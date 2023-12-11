#!/bin/bash

cd Aardium
npm install

os=$(uname -s)
if [ "$os" == "Darwin" ];
then
    arch=$1;
    if [ "$arch" == "" ];
    then
        arch=$(uname -m)
    fi

    if [ "$arch" == "x86_64" ];
    then
        npm run dist:darwin:x64
    else
        npm run dist:darwin:arm64
    fi

else
    npm run dist:linux:x64
fi