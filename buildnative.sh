cd Aardium

os=$(uname -s)
arch=$1;
if [ "$arch" == "" ];
then
    arch=$(uname -m)
fi

if [ $os == "Darwin" ];
then
    if [ $arch == "x86_64" ];
    then
        yarn dist:darwin:x64
    else
        yarn dist:darwin:arm64
    fi
else
        yarn dist:linux
fi