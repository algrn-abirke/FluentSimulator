﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentSim;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NUnit.Framework;
using RestSharp;
using Shouldly;

namespace FluentSimTests
{
  public class FluentSimTests
  {
    private FluentSimulator Sim;
    private const string BaseAddress = "http://localhost:8019/";

    [SetUp]
    public void SetUp()
    {
      Sim = new FluentSimulator(BaseAddress);
      Sim.Start();
    }

    [TearDown]
    public void TearDown()
    {
      Sim.Stop();
    }

    [Test]
    public void CanMakeGetRequest()
    {
      Sim.Get("/test")
        .Responds("TEST");

      var resp = MakeGetRequest("/test");
      resp.Content.ShouldBe("TEST");
      resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static IRestResponse MakeGetRequest(string path)
    {
      var request = new RestRequest(path, Method.GET);
      var client = new RestClient(BaseAddress);
      var resp = client.Execute(request);
      return resp;
    }

    private static IRestResponse MakePostRequest(string path, object body)
    {
      var verb = Method.POST;
      return MakeRequest(path, verb, body);
    }

    private static IRestResponse MakeRequest(string path, Method verb, object body = null)
    {
      var resp = MakeRawRequest(path, verb, body);
      return resp;
    }

    private static IRestResponse MakeRawRequest(string path, Method verb, object body = null)
    {
      var request = new RestRequest(path, verb);
      if (body != null) request.AddParameter("text/json", body, ParameterType.RequestBody);
      var client = new RestClient(BaseAddress);
      var resp = client.Execute(request);
      return resp;
    }

    [Test]
    public void CanMakeGetRequestsOnDifferentUrls()
    {
      Sim.Get("/test1").Responds("output1");
      Sim.Get("/test2").Responds("output2");

      MakeGetRequest("/test1").Content.ShouldBe("output1");
      MakeGetRequest("/test2").Content.ShouldBe("output2");
    }

    [Test]
    public void RequestingAUrlThatDoesNotExistReturns501()
    {
      MakeGetRequest("/testUrl").StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Test]
    public void CanMakeRequestAfterRequestingAUrlThatIsNotConfigured()
    {
      Sim.Get("/test").Responds("out");
      MakeGetRequest("/nonexistent");
      MakeGetRequest("/test").Content.ShouldBe("out");
    }

    [Test]
    public void WhenARouteEndsInASlashTheUrlIsRecognised()
    {
      Sim.Get("/test").Responds("out");
      MakeGetRequest("/test/").Content.ShouldBe("out");
    }

    [Test]
    public void WhenTheTargetEndsInSometingDifferentItDoesNotMatchTheConfiguredRoute()
    {
      Sim.Get("/test").Responds("out");
      MakeGetRequest("/test/somethingelse").StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Test]
    public void WhenTargetStartsWithSometingDifferentItDoesNotMatch()
    {
      Sim.Get("/test").Responds("out");
      MakeGetRequest("something/test").StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Test]
    public void WhenTheRouteIsADifferentVerbItIsNotMatched()
    {
      Sim.Get("/test").Responds("outhere");
      MakePostRequest("/test", "").StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Test]
    public void CanMakePostRequest()
    {
      Sim.Post("/test").Responds("outhere");
      MakePostRequest("/test", "").Content.ShouldBe("outhere");
    }

    [Test]
    public void ThePauseAndResumeWorks()
    {
      var route = Sim.Get("/path").Responds("SOMEOUTPUT");
      route.Pause();
      var timer = new Stopwatch();
      timer.Start();
      ResumeTheRouteInHalfASecond(route);
      MakeGetRequest("/path").Content.ShouldBe("SOMEOUTPUT");
      timer.Stop();
      timer.ElapsedMilliseconds.ShouldBeGreaterThan(499);
    }

    [Test]
    public void TheTimeDelayWorks()
    {
      Sim.Get("/path").Delay(TimeSpan.FromMilliseconds(500))
        .Responds("delayed");
      var stopwatch = new Stopwatch();
      stopwatch.Start();
      MakeGetRequest("/path").Content.ShouldBe("delayed");
      stopwatch.Stop();
      stopwatch.ElapsedMilliseconds.ShouldBeGreaterThan(500);
    }

    private static void ResumeTheRouteInHalfASecond(RouteConfigurer route)
    {
      new Thread(() =>
      {
        Thread.Sleep(500);
        route.Resume();
      }).Start();
    }

    [Test]
    public void CanRespondWithCodes()
    {
      Sim.Post("/test").Responds().WithCode(300);
      MakePostRequest("/test", "").StatusCode.ShouldBe(HttpStatusCode.Ambiguous);
    }

    [Test]
    public void CanRespondWithHeaders()
    {
      Sim.Post("/test").Responds().WithHeader("ThisHeader", "ThisValue");
      var resp = MakePostRequest("/test", "");

      // the response will contain a few default headers,
      // make sure it contains the custom header from above, too
      var customHeader = resp.Headers.Single(header => header.Name.Equals("ThisHeader"));
      Assert.NotNull(customHeader);
      Assert.AreEqual("ThisValue", customHeader.Value);
    }

    [Test]
    public void CanRespondWithCookies()
    {
      Sim.Get("/test").Responds().WithCookie(new Cookie("name", "VALTEST"));
      var resp = MakeGetRequest("/test");
      var cookie = resp.Cookies[0];
      cookie.Name.ShouldBe("name");
      cookie.Value.ShouldBe("VALTEST");
    }

    [Test]
    public void CanReturnSerialisedObjects()
    {
      Sim.Post("/test")
        .Responds(new TestObject());

      MakePostRequest("/test", "").Content.ShouldBe(@"{""TestField"":""ThisValue""}");
    }

    [Test]
    public void CanGetBinaryDataObject()
    {
      Sim.Post("/test")
        .Responds(new byte[] {1, 2, 3, 4});

      MakeRawRequest("/test", Method.POST).RawBytes.ShouldBe(new byte[] {1, 2, 3, 4});
    }

    [Test]
    public void CanUseCustomSerializer()
    {
      Sim.Stop();
      Sim = new FluentSimulator(BaseAddress, new JsonSerializerSettings
      {
        Converters = {new StringEnumConverter()}
      });
      Sim.Start();

      Sim.Post("/test")
        .Responds(new TestEnumClass());

      MakePostRequest("/test", "").Content.ShouldBe(@"{""TestEnumField"":""V2""}");
    }

    [Test]
    public void CanMatchRequestCaseInsensitively()
    {
      Sim.Get("/test").Responds("out");
      MakeGetRequest("/TEST").Content.ShouldBe("out");
    }

    [Test]
    public void CanMakeOtherVerbRequests()
    {
      MakeVerbRequest(Sim.Post("/test"), Method.POST);
      MakeVerbRequest(Sim.Get("/test"), Method.GET);
      MakeVerbRequest(Sim.Delete("/test"), Method.DELETE);
      MakeVerbRequest(Sim.Head("/test"), Method.HEAD);
      MakeVerbRequest(Sim.Merge("/test"), Method.MERGE);
      MakeVerbRequest(Sim.Options("/test"), Method.OPTIONS);
      MakeVerbRequest(Sim.Patch("/test"), Method.PATCH);
      MakeVerbRequest(Sim.Put("/test"), Method.PUT);
    }

    [Test]
    public void CanMakeRegexRequest()
    {
      Sim.Get("/test[0-9]").MatchingRegex().Responds("output1");
      MakeGetRequest("/test1").Content.ShouldBe("output1");
    }

    [Test]
    public void CanMakeComplexRegexRequest()
    {
      Sim.Get(@"api\/.*\/read\/Business\/Business.*").MatchingRegex().Responds("Output");
      MakeGetRequest("/api/02b9863e-cb07-c62d-50a9-8ecb40aa85b6/read/Business/Business/").Content.ShouldBe("Output");
    }

    [Test]
    public void CanMatchRegexNotEndingInSlash()
    {
      Sim.Get("/test[0-9]").MatchingRegex().Responds("output1");
      MakeGetRequest("/test1sdfsdfds").Content.ShouldBe("output1");
    }

    private void MakeVerbRequest(RouteConfigurer configurer, Method verb)
    {
      if (verb == Method.HEAD)
      {
        configurer.Responds("");
        MakeRequest("/test", verb).StatusCode.ShouldBe(HttpStatusCode.OK);
        return;
      }

      configurer.Responds(verb + "output");
      MakeRequest("/test", verb).Content.ShouldBe(verb + "output");
    }

    [Test]
    public void CanGetPreviousRequests()
    {
      Sim.Post("/post").Responds("OK");
      MakePostRequest("/post", "BODY");

      var requests = Sim.ReceivedRequests;
      requests.Count.ShouldBe(1);
      var firstRequest = requests[0];
      firstRequest.AcceptTypes.Length.ShouldBe(6);
      firstRequest.Url.AbsoluteUri.ShouldBe("http://localhost:8019/post");
      firstRequest.UserAgent.ShouldStartWith("RestSharp");
    }

    [Test]
    public void CanGetTimeOfPreviousRequest()
    {
      Sim.Post("/post").Responds("OK");
      MakePostRequest("/post", "BODY");
      var timeOfRequest = DateTime.Now;

      Thread.Sleep(300);

      var requests = Sim.ReceivedRequests;
      var firstRequest = requests[0];
      var diff = (firstRequest.TimeOfRequest - timeOfRequest);
      Math.Abs(diff.TotalMilliseconds).ShouldBeLessThan(10);
    }

    [Test]
    public void CanGetPreviousRequestBodyAsString()
    {
      Sim.Post("/post").Responds("OK");
      MakePostRequest("/post", "BODY");

      var requests = Sim.ReceivedRequests;
      var firstRequest = requests[0];
      firstRequest.RequestBody.ShouldBe("BODY");
    }

    [Test]
    public void CanGetPreviousRequestBodyAsObject()
    {
      Sim.Post("/post").Responds("OK");
      MakePostRequest("/post", @"{""TestField"":""TESTHERE""}");

      var requests = Sim.ReceivedRequests;
      var firstRequest = requests[0];
      firstRequest.BodyAs<TestObject>().TestField.ShouldBe("TESTHERE");
    }

    [Test]
    public void CanGetPreviousBodyWithCustomSerializer()
    {
      Sim.Stop();
      Sim = new FluentSimulator(BaseAddress, new JsonSerializerSettings
      {
        Converters = {new AllFieldsReplacementConverter()}
      });
      Sim.Start();

      Sim.Post("/test");

      MakePostRequest("/test", @"{""TestField"":""original""}");

      Sim.ReceivedRequests[0].BodyAs<TestObject>().TestField.ShouldBe("REPLACEMENT");
    }

    [Test]
    public void CanAddCorsHeaders()
    {
      Sim.EnableCors();
      Sim.Post("/test");
      var resp = MakePostRequest("/test", @"{""TestField"":""original""}");
      AssertResponseContainsCorsHeaders(resp);
    }

    private static void AssertResponseContainsCorsHeaders(IRestResponse resp)
    {
      resp.Headers.First(n => n.Name == "Access-Control-Allow-Origin").Value.ShouldBe("*");
      resp.Headers.First(n => n.Name == "Access-Control-Allow-Headers").Value
        .ShouldBe("Authorization, Content-Type");
    }

    [Test]
    public void CanRespondToPreflightWithCorsHeaders()
    {
      Sim.EnableCors();
      Sim.Post("/test").Responds("TEST OUTPUT");
      var resp = MakeRequest("/test", Method.OPTIONS);
      AssertResponseContainsCorsHeaders(resp);

      MakePostRequest("/test", new object()).Content.ShouldBe("TEST OUTPUT");
    }

    [Test]
    public void CanGetTheRequestsToAMatchingRoute()
    {
      var post = Sim.Post("/test").Responds("POST").History();
      var get = Sim.Get("/test").Responds("GET").History();
      MakePostRequest("/test", @"{""TestField"":""original""}");
      MakeRequest("/test", Method.GET);
      MakeRequest("/test", Method.GET);
      get.ReceivedRequests.Count.ShouldBe(2);
      post.ReceivedRequests.Count.ShouldBe(1);
      post.ReceivedRequests[0].BodyAs<TestObject>().TestField.ShouldBe("original");
    }

    [Test]
    public void SequentialSetupsOverwriteThePreviousSetups()
    {
      Sim.Post("/post").Responds("OK");
      Sim.Post("/post").Responds("OK2");
      MakePostRequest("/post", "").Content.ShouldBe("OK2");
    }

    [Test]
    public void CanMakeGetRequestWithQueryString()
    {
      var queryString = "/test?key=value";
      Sim.Get("/test").WithParameter("key", "value").Responds("OK");
      var result = MakeGetRequest(queryString);
      result.Content.ShouldBe("OK");
    }

    [Test]
    public void CanMakeGetRequestWithMultipleQueryStringsOutOfOrder()
    {
      var queryString = "/test?key=value&key1=value1&key2=value2";
      Sim.Get("/test")
        .WithParameter("key", "value")
        .WithParameter("key2", "value2")
        .WithParameter("key1", "value1")
        .Responds("OK");
      var result = MakeGetRequest(queryString);
      result.Content.ShouldBe("OK");
    }

    [Test]
    public void GivenTheQueryStringIsLongerThanWhatWasExpected_Fails()
    {
      var queryString = "/test?key=value&key1=value1&key2=value2";
      Sim.Get("/test")
        .WithParameter("key1", "value1")
        .WithParameter("key", "value")
        .Responds("OK");
      var result = MakeGetRequest(queryString);
      result.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Test]
    public void GivenTheExpectedParametersAreLongerThanTheQueryString_Fails()
    {
      var queryString = "/test?key=value";
      Sim.Get("/test")
        .WithParameter("key", "value")
        .WithParameter("key2", "value2")
        .WithParameter("key1", "value1")
        .Responds("OK");
      var result = MakeGetRequest(queryString);
      result.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }


    [TestCase("value%20here", "value here")]
    [TestCase("value+here", "value here")]
    [TestCase("Some%25%5E%26*(value", "Some%^&*(value")]
    public void GivenTheQueryParamsHaveUrlEncodedCharactersItComparesOnUrlDecodedValues(string encoded, string decoded)
    {
      var queryString = "/test?key=" + encoded;
      Sim.Get("/test")
        .WithParameter("key", decoded)
        .Responds("OK");
      var result = MakeGetRequest(queryString);
      result.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
    
    [Test]
    public void CanUseLambdaFunctionToRespondAtRuntime()
    {
      var counter = 0;
      Sim.Post("/post").IsHandledBy(r => $"Counter: {counter++}");
      
      var resp1 = MakePostRequest("/post", "BODY");
      var resp2 = MakePostRequest("/post", "BODY");
      resp1.Content.ShouldBe("Counter: 0");
      resp2.Content.ShouldBe("Counter: 1");
    }
    
    [Test]
    public void LambdaHandlerReceivesCorrectRequestInfo()
    {
      var counter = 0;
      Sim.Post("/post").IsHandledBy(r => $"Req: {r.RequestBody}");
      
      var resp1 = MakePostRequest("/post", "BODY");
      resp1.Content.ShouldBe("Req: BODY");
    }

    private class AllFieldsReplacementConverter : JsonConverter
    {
      public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
      {
      }

      public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
        JsonSerializer serializer)
      {
        return "REPLACEMENT";
      }

      public override bool CanConvert(Type objectType)
      {
        return objectType == typeof(string);
      }
    }

    private class TestObject
    {
      public string TestField = "ThisValue";
    }

    private class TestEnumClass
    {
      public TestEnum TestEnumField = TestEnum.V2;

      public enum TestEnum
      {
        V1,
        V2
      }
    }
  }
}