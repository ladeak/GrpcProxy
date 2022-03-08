dotnet clean GrpcProxy.csproj --configuration Release --framework net6.0
dotnet publish GrpcProxy.csproj --configuration Release --no-self-contained --framework net6.0
nuget pack ./bin/Release/net6.0/publish -OutputDirectory ./bin/Release/net6.0/publish