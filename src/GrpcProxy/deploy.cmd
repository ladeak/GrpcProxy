.\build.cmd
dotnet tool uninstall grpcproxy --global
dotnet tool install GrpcProxy --add-source .\bin\Release\net6.0\publish --global
grpcproxy