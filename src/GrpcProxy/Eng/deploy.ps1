.\Eng\build.ps1
dotnet tool uninstall LaDeak.GrpcProxy --global
dotnet tool install LaDeak.GrpcProxy --add-source .\bin\Release\net6.0\publish --global --version 0.0.1-dev
grpcproxy