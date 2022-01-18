#!/bin/sh

cd Aardium
npm install -g yarn
yarn install

os=$(uname -s)
if [ $os == "Darwin" ];
then
    arch=$1;
    if [ "$arch" == "" ];
    then
        arch=$(uname -m)
    fi

    if [ $arch == "x86_64" ];
    then
        yarn dist:darwin:x64
    else
        yarn dist:darwin:arm64
    fi

else
    yarn dist:linux:x64
fi