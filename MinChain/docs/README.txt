1. how to run

cd ./MinChain/MinChain/bin/Debug/netcoreapp2.0

dotnet MinChain.dll genkey > key.json
dotnet MinChain.dll genesis key.json genesis.bin
dotnet MinChain.dll config
dotnet MinChain.dll config > config.json
dotnet MinChain.dll run config.json 
