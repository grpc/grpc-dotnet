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

using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Net.Client;
using Issue;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Web.Client
{
    [TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http1)]
    [TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http2)]
    [TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http1)]
    [TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http2)]
    [TestFixture(GrpcTestMode.Grpc, TestServerEndpointName.Http2)]
    public class IssueTests : GrpcWebFunctionalTestBase
    {
        public IssueTests(GrpcTestMode grpcTestMode, TestServerEndpointName endpointName)
         : base(grpcTestMode, endpointName)
        {
        }

        // https://github.com/grpc/grpc-dotnet/issues/752
        [Test]
        public async Task SendLargeRequest_SuccessResponse()
        {
            // Arrage
            var httpClient = CreateGrpcWebClient();
            var channel = GrpcChannel.ForAddress(httpClient.BaseAddress, new GrpcChannelOptions
            {
                HttpClient = httpClient,
                LoggerFactory = LoggerFactory
            });

            var client = new IssueService.IssueServiceClient(channel);
            var request = JsonParser.Default.Parse<GetLibraryRequest>(JsonRequest);

            // Act
            var response = await client.GetLibraryAsync(request);

            // Assert
            Assert.AreEqual("admin", response.UserId);
        }

        private const string JsonRequest = @"{
  ""userId"": ""admin"",
  ""carriers"": [
    ""273"",
    ""274"",
    ""275"",
    ""276"",
    ""277"",
    ""278"",
    ""279"",
    ""280"",
    ""281"",
    ""282"",
    ""283"",
    ""284"",
    ""285"",
    ""286"",
    ""287"",
    ""288"",
    ""289"",
    ""290"",
    ""291"",
    ""293"",
    ""294"",
    ""295"",
    ""296"",
    ""297"",
    ""298"",
    ""299"",
    ""301"",
    ""302"",
    ""303"",
    ""304"",
    ""305"",
    ""111"",
    ""112"",
    ""113"",
    ""115"",
    ""116"",
    ""117"",
    ""118"",
    ""119"",
    ""120"",
    ""121"",
    ""122"",
    ""123"",
    ""124"",
    ""125"",
    ""126"",
    ""127"",
    ""128"",
    ""130"",
    ""131"",
    ""132"",
    ""133"",
    ""134"",
    ""369"",
    ""370"",
    ""371"",
    ""372"",
    ""373"",
    ""374"",
    ""375"",
    ""376"",
    ""377"",
    ""378"",
    ""379"",
    ""380"",
    ""381"",
    ""382"",
    ""383"",
    ""384"",
    ""385"",
    ""386"",
    ""387"",
    ""388"",
    ""389"",
    ""390"",
    ""391"",
    ""392"",
    ""393"",
    ""394"",
    ""395"",
    ""396"",
    ""397"",
    ""398"",
    ""399"",
    ""400"",
    ""401"",
    ""638"",
    ""639"",
    ""640"",
    ""641"",
    ""642"",
    ""643"",
    ""644"",
    ""645"",
    ""646"",
    ""647"",
    ""648"",
    ""649"",
    ""650"",
    ""651"",
    ""652"",
    ""653"",
    ""654"",
    ""655"",
    ""656"",
    ""657"",
    ""658"",
    ""659"",
    ""660"",
    ""661"",
    ""662"",
    ""663"",
    ""664"",
    ""665"",
    ""666"",
    ""667"",
    ""668"",
    ""670"",
    ""671"",
    ""672"",
    ""673"",
    ""675"",
    ""676"",
    ""677"",
    ""698"",
    ""700"",
    ""701"",
    ""702"",
    ""703"",
    ""704"",
    ""518"",
    ""519"",
    ""520"",
    ""521"",
    ""522"",
    ""523"",
    ""524"",
    ""525"",
    ""526"",
    ""527"",
    ""528"",
    ""529"",
    ""530"",
    ""531"",
    ""532"",
    ""533"",
    ""534"",
    ""535"",
    ""536"",
    ""537"",
    ""538"",
    ""539"",
    ""540"",
    ""541"",
    ""88"",
    ""89"",
    ""90"",
    ""91"",
    ""92"",
    ""93"",
    ""94"",
    ""95"",
    ""96"",
    ""97"",
    ""98"",
    ""99"",
    ""100"",
    ""101"",
    ""102"",
    ""103"",
    ""104"",
    ""105"",
    ""106"",
    ""107"",
    ""108"",
    ""110"",
    ""1"",
    ""2"",
    ""3"",
    ""4"",
    ""5"",
    ""6"",
    ""7"",
    ""8"",
    ""9"",
    ""10"",
    ""11"",
    ""12"",
    ""13"",
    ""14"",
    ""15"",
    ""16"",
    ""17"",
    ""18"",
    ""19"",
    ""20"",
    ""21"",
    ""22"",
    ""23"",
    ""24"",
    ""25"",
    ""26"",
    ""27"",
    ""28"",
    ""29"",
    ""30"",
    ""31"",
    ""32"",
    ""33"",
    ""34"",
    ""35"",
    ""36"",
    ""37"",
    ""38"",
    ""39"",
    ""40"",
    ""41"",
    ""42"",
    ""43"",
    ""44"",
    ""45"",
    ""46"",
    ""47"",
    ""48"",
    ""49"",
    ""50"",
    ""51"",
    ""52"",
    ""53"",
    ""54"",
    ""55"",
    ""56"",
    ""57"",
    ""58"",
    ""59"",
    ""60"",
    ""61"",
    ""62"",
    ""63"",
    ""64"",
    ""65"",
    ""66"",
    ""67"",
    ""68"",
    ""69"",
    ""70"",
    ""71"",
    ""72"",
    ""73"",
    ""74"",
    ""75"",
    ""76"",
    ""77"",
    ""78"",
    ""79"",
    ""80"",
    ""81"",
    ""82"",
    ""83"",
    ""84"",
    ""85"",
    ""86"",
    ""87"",
    ""265"",
    ""266"",
    ""267"",
    ""268"",
    ""269"",
    ""270"",
    ""271"",
    ""272"",
    ""629"",
    ""630"",
    ""631"",
    ""633"",
    ""634"",
    ""635"",
    ""636"",
    ""637"",
    ""705"",
    ""706"",
    ""707"",
    ""710"",
    ""711"",
    ""712"",
    ""713"",
    ""714"",
    ""715"",
    ""716"",
    ""717"",
    ""718"",
    ""719"",
    ""720"",
    ""721"",
    ""722"",
    ""723"",
    ""724"",
    ""725"",
    ""135"",
    ""136"",
    ""138"",
    ""139"",
    ""141"",
    ""142"",
    ""144"",
    ""145"",
    ""146"",
    ""150"",
    ""151"",
    ""152"",
    ""153"",
    ""154"",
    ""155"",
    ""156"",
    ""157"",
    ""158"",
    ""159"",
    ""160"",
    ""161"",
    ""162"",
    ""164"",
    ""170"",
    ""171"",
    ""172"",
    ""173"",
    ""184"",
    ""185"",
    ""189"",
    ""196"",
    ""197"",
    ""198"",
    ""199"",
    ""200"",
    ""201"",
    ""202"",
    ""203"",
    ""204"",
    ""205"",
    ""206"",
    ""207"",
    ""209"",
    ""210"",
    ""211"",
    ""212"",
    ""213"",
    ""215"",
    ""216"",
    ""217"",
    ""218"",
    ""219"",
    ""220"",
    ""221"",
    ""222"",
    ""223"",
    ""224"",
    ""225"",
    ""226"",
    ""227"",
    ""228"",
    ""229"",
    ""230"",
    ""231"",
    ""232"",
    ""233"",
    ""234"",
    ""235"",
    ""236"",
    ""237"",
    ""238"",
    ""239"",
    ""240"",
    ""241"",
    ""242"",
    ""243"",
    ""244"",
    ""245"",
    ""246"",
    ""247"",
    ""248"",
    ""249"",
    ""250"",
    ""251"",
    ""252"",
    ""253"",
    ""254"",
    ""255"",
    ""256"",
    ""257"",
    ""258"",
    ""259"",
    ""260"",
    ""261"",
    ""262"",
    ""263"",
    ""264"",
    ""402"",
    ""403"",
    ""404"",
    ""405"",
    ""406"",
    ""407"",
    ""408"",
    ""409"",
    ""410"",
    ""411"",
    ""412"",
    ""413"",
    ""414"",
    ""415"",
    ""416"",
    ""417"",
    ""418"",
    ""419"",
    ""420"",
    ""421"",
    ""422"",
    ""423"",
    ""424"",
    ""425"",
    ""472"",
    ""473"",
    ""474"",
    ""475"",
    ""476"",
    ""477"",
    ""478"",
    ""479"",
    ""480"",
    ""481"",
    ""482"",
    ""483"",
    ""484"",
    ""485"",
    ""486"",
    ""487"",
    ""488"",
    ""489"",
    ""490"",
    ""491"",
    ""492"",
    ""493"",
    ""494"",
    ""495"",
    ""496"",
    ""497"",
    ""498"",
    ""499"",
    ""500"",
    ""501"",
    ""502"",
    ""503"",
    ""504"",
    ""505"",
    ""506"",
    ""507"",
    ""508"",
    ""509"",
    ""510"",
    ""511"",
    ""512"",
    ""513"",
    ""514"",
    ""515"",
    ""516"",
    ""517"",
    ""426"",
    ""427"",
    ""428"",
    ""429"",
    ""430"",
    ""431"",
    ""432"",
    ""433"",
    ""434"",
    ""435"",
    ""436"",
    ""437"",
    ""438"",
    ""439"",
    ""440"",
    ""441"",
    ""442"",
    ""443"",
    ""444"",
    ""445"",
    ""446"",
    ""859"",
    ""448"",
    ""449"",
    ""450"",
    ""451"",
    ""452"",
    ""453"",
    ""454"",
    ""455"",
    ""456"",
    ""457"",
    ""458"",
    ""459"",
    ""460"",
    ""461"",
    ""462"",
    ""464"",
    ""465"",
    ""466"",
    ""467"",
    ""468"",
    ""469"",
    ""470"",
    ""471"",
    ""307"",
    ""308"",
    ""309"",
    ""310"",
    ""325"",
    ""326"",
    ""327"",
    ""328"",
    ""329"",
    ""330"",
    ""331"",
    ""332"",
    ""333"",
    ""334"",
    ""335"",
    ""336"",
    ""337"",
    ""360"",
    ""361"",
    ""362"",
    ""363"",
    ""364"",
    ""365"",
    ""366"",
    ""367"",
    ""368"",
    ""542"",
    ""543"",
    ""544"",
    ""545"",
    ""546"",
    ""547"",
    ""548"",
    ""549"",
    ""550"",
    ""551"",
    ""552"",
    ""553"",
    ""554"",
    ""555"",
    ""556"",
    ""557"",
    ""558"",
    ""559"",
    ""560"",
    ""561"",
    ""562"",
    ""563"",
    ""564"",
    ""565"",
    ""566"",
    ""567"",
    ""568"",
    ""569"",
    ""570"",
    ""571"",
    ""572"",
    ""573"",
    ""574"",
    ""575"",
    ""576"",
    ""577"",
    ""578"",
    ""579"",
    ""580"",
    ""581"",
    ""582"",
    ""583"",
    ""584"",
    ""585"",
    ""586"",
    ""587"",
    ""588"",
    ""589"",
    ""590"",
    ""591"",
    ""592"",
    ""593"",
    ""594"",
    ""595"",
    ""596"",
    ""597"",
    ""598"",
    ""599"",
    ""600"",
    ""601"",
    ""602"",
    ""603"",
    ""604"",
    ""605"",
    ""606"",
    ""607"",
    ""608"",
    ""609"",
    ""610"",
    ""611"",
    ""613"",
    ""614"",
    ""615"",
    ""616"",
    ""617"",
    ""618"",
    ""619"",
    ""620"",
    ""621"",
    ""622"",
    ""623"",
    ""624"",
    ""625"",
    ""626"",
    ""627"",
    ""628"",
    ""744"",
    ""8001"",
    ""8004"",
    ""8005"",
    ""678"",
    ""756"",
    ""749"",
    ""339"",
    ""750"",
    ""752"",
    ""764"",
    ""758"",
    ""726"",
    ""729"",
    ""731"",
    ""995"",
    ""8010"",
    ""8011"",
    ""762"",
    ""730"",
    ""734"",
    ""737"",
    ""740"",
    ""743"",
    ""746"",
    ""748"",
    ""8002"",
    ""753"",
    ""754"",
    ""761"",
    ""727"",
    ""728"",
    ""733"",
    ""735"",
    ""736"",
    ""741"",
    ""341"",
    ""747"",
    ""8003"",
    ""755"",
    ""342"",
    ""757"",
    ""8008"",
    ""760"",
    ""766"",
    ""732"",
    ""338"",
    ""343"",
    ""763"",
    ""765"",
    ""738"",
    ""739"",
    ""742"",
    ""745"",
    ""340"",
    ""751"",
    ""8006"",
    ""8007"",
    ""759"",
    ""44444"",
    ""447"",
    ""463"",
    ""153"",
    ""767"",
    ""59"",
    ""838"",
    ""839"",
    ""840"",
    ""841"",
    ""842"",
    ""844"",
    ""845"",
    ""846"",
    ""848"",
    ""849"",
    ""850"",
    ""851"",
    ""853"",
    ""854"",
    ""855"",
    ""856"",
    ""857"",
    ""858"",
    ""859"",
    ""860"",
    ""861"",
    ""862"",
    ""863"",
    ""864"",
    ""865"",
    ""866"",
    ""867"",
    ""868"",
    ""870"",
    ""871"",
    ""872"",
    ""873"",
    ""874"",
    ""879"",
    ""882"",
    ""883"",
    ""885"",
    ""886"",
    ""887"",
    ""888"",
    ""890"",
    ""891"",
    ""892"",
    ""894"",
    ""896"",
    ""897"",
    ""898"",
    ""899"",
    ""713"",
    ""800"",
    ""801"",
    ""802"",
    ""803"",
    ""804"",
    ""805"",
    ""806"",
    ""807"",
    ""808"",
    ""809"",
    ""810"",
    ""811"",
    ""812"",
    ""813"",
    ""814"",
    ""815"",
    ""816"",
    ""817"",
    ""818"",
    ""819"",
    ""820"",
    ""821"",
    ""822"",
    ""823"",
    ""824"",
    ""825"",
    ""826"",
    ""827"",
    ""828"",
    ""830"",
    ""831"",
    ""832"",
    ""833"",
    ""834"",
    ""836"",
    ""190"",
    ""426"",
    ""877"",
    ""881"",
    ""8000"",
    ""875"",
    ""884"",
    ""880"",
    ""895"",
    ""878"",
    ""8012"",
    ""679"",
    ""771"",
    ""680"",
    ""847"",
    ""769"",
    ""772"",
    ""837"",
    ""893"",
    ""876"",
    ""889"",
    ""8013"",
    ""770""
  ]
}";
    }
}
