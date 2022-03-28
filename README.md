# GrpcProxy

This tool is under development.

## Getting Started

Install the tool with dotnet CLI:

```
dotnet tool install LaDeak.GrpcProxy --global --version 0.0.2-alpha2
```

### Run the tool

```
grpcproxy --p:ProtoPath .\\SuperService.proto --p:Address https://localhost:5001
```

A web UI is available at https://localhost:7012.

## Run the tool without a proto file

```
grpcproxy --GrpcProxy:FallbackAddress https://localhost:5001
```

### Parameters

- `--p:ProtoPath` [optional] defines a default proto file to use
- `--p:Address` [optional] defines where the proxy service shall forward the requests
- `--p:SeqAddress` [optional] if logs shall be pushes to SEQ
- `--p:SeqKey` [optional] SEQ API key

### Response Mocking

Response mocking is only supported for GRPC calls with proto file configured. Set one or multiple (by incrementing the counter `0`) mock messages for responses. When RPC call is streaming response or duplex only a single message is returned to the called.

- `--p:MockResponses:0:MethodName [Name Of The Method To Mock]` - defines the RPC's method name to mock
- `--p:MockResponses:0:Response "{\"Message\":\"This is a mocked response\"}"` - JsonSerialized string value of the mocked data


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