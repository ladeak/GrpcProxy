# GrpcProxy

This tool is under development.

## Getting Started

Install the tool with dotnet CLI:

```
dotnet tool install LaDeak.GrpcProxy --global --version 0.0.1-alpha7
```

Run the tool as:

```
grpcproxy --p:ProtoPath .\\SuperService.proto --p:Address https://localhost:5001
```

Run the tool without a proto file:

```
grpcproxy --GrpcProxy:FallbackAddress https://localhost:5001
```

- `--p:ProtoPath` [optional] defines a default proto file to use
- `--p:Address` [optional] defines where the proxy service shall forward the requests

A web UI is available at https://localhost:7012.


## Grpc-DotNet Subtree

Grpc-DotNet is a subtree in this repository. Pull latest with the following command:

```
git subtree pull --prefix grpc-dotnet https://github.com/grpc/grpc-dotnet.git master --squash
```

## Troubleshooting

The tool uses dotnet dev certificates that are installed with .NET SDK. If the certificates are not yet trusted run the following command:

```
dotnet dev-certs https --trust
```