#!/bin/sh

dotnet build -c Release --no-restore &
dotnet publish src/Autopelago/Autopelago.csproj -c Release -r win-x64 -o artifacts/win-x64 &
dotnet publish src/Autopelago/Autopelago.csproj -c Release -r linux-x64 -o artifacts/linux-x64 &

wait
