#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Grpc.Net.ClientFactory.Tests,PublicKey=" +
    "00240000048000009400000006020000002400005253413100040000010001002f5797a92c6fcde81bd4098f43" +
    "0442bb8e12768722de0b0cb1b15e955b32a11352740ee59f2c94c48edc8e177d1052536b8ac651bce11ce5da3a" +
    "27fc95aff3dc604a6971417453f9483c7b5e836756d5b271bf8f2403fe186e31956148c03d804487cf642f8cc0" +
    "71394ee9672dfe5b55ea0f95dfd5a7f77d22c962ccf51320d3")]

[assembly: InternalsVisibleTo("Grpc.AspNetCore.Server.ClientFactory.Tests,PublicKey=" +
    "00240000048000009400000006020000002400005253413100040000010001002f5797a92c6fcde81bd4098f43" +
    "0442bb8e12768722de0b0cb1b15e955b32a11352740ee59f2c94c48edc8e177d1052536b8ac651bce11ce5da3a" +
    "27fc95aff3dc604a6971417453f9483c7b5e836756d5b271bf8f2403fe186e31956148c03d804487cf642f8cc0" +
    "71394ee9672dfe5b55ea0f95dfd5a7f77d22c962ccf51320d3")]

// For Moq. This assembly needs access to internal types via InternalVisibleTo to be able to mock them
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2, PublicKey=0024000004800000940000000602" +
    "000000240000525341310004000001000100c547cac37abd99c8db225ef2f6c8a3602f3b3606cc9891605d02ba" +
    "a56104f4cfc0734aa39b93bf7852f7d9266654753cc297e7d2edfe0bac1cdcf9f717241550e0a7b191195b7667" +
    "bb4f64bcb8e2121380fd1d9d46ad2d92d2d15605093924cceaf74c4861eff62abf69b9291ed0a340e113be11e6" +
    "a7d3113e92484cf7045cc7")] 