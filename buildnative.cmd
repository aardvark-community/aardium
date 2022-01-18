@echo off


pushd Aardium
npm install -g yarn
yarn install
yarn dist:win32:x64