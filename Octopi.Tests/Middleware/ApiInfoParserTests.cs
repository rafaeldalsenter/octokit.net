﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using Octopi.Http;
using Xunit;
using Xunit.Extensions;

namespace Octopi.Tests
{
    public class ApiInfoParserTests
    {
        public class TheConstructor
        {
            [Fact]
            public void ThrowsForBadArguments()
            {
                Assert.Throws<ArgumentNullException>(() => new ApiInfoParser(null));
            }
        }

        public class TheAfterMethod
        {
            [Fact]
            public async Task ParsesApiInfoFromHeaders()
            {
                var env = new Environment<string> { Response = new GitHubResponse<string>() };
                env.Response.Headers.Add("X-Accepted-OAuth-Scopes", "user");
                env.Response.Headers.Add("X-OAuth-Scopes", "user, public_repo, repo, gist");
                env.Response.Headers.Add("X-RateLimit-Limit", "5000");
                env.Response.Headers.Add("X-RateLimit-Remaining", "4997");
                env.Response.Headers.Add("ETag", "5634b0b187fd2e91e3126a75006cc4fa");
                var app = Substitute.For<IApplication>();
                app.Invoke(env).Returns(Task.FromResult(app));

                var parser = new ApiInfoParser(app);

                await parser.Invoke(env);

                var i = ((GitHubResponse<string>)env.Response).ApiInfo;
                i.Should().NotBeNull();
                i.AcceptedOauthScopes.Should().BeEquivalentTo(new[] { "user" });
                i.OauthScopes.Should().BeEquivalentTo(new[] { "user", "public_repo", "repo", "gist" });
                i.RateLimit.Should().Be(5000);
                i.RateLimitRemaining.Should().Be(4997);
                i.Etag.Should().Be("5634b0b187fd2e91e3126a75006cc4fa");
            }

            [Fact]
            public async Task BadHeadersAreIgnored()
            {
                var env = new Environment<string> { Response = new GitHubResponse<string>() };
                env.Response.Headers.Add("Link", "<https://api.github.com/repos/rails/rails/issues?page=4&per_page=5>; , <https://api.github.com/repos/rails/rails/issues?page=131&per_page=5; rel=\"last\"");
                var app = Substitute.For<IApplication>();
                app.Invoke(env).Returns(Task.FromResult(app));
                var parser = new ApiInfoParser(app);

                await parser.Invoke(env);

                var i = env.Response.ApiInfo;
                i.Should().NotBeNull();
                i.Links.Count.Should().Be(0);
            }

            [Fact]
            public async Task ParsesLinkHeader()
            {
                var env = new Environment<string> { Response = new GitHubResponse<string>() };
                env.Response.Headers.Add("Link", "<https://api.github.com/repos/rails/rails/issues?page=4&per_page=5>; rel=\"next\", <https://api.github.com/repos/rails/rails/issues?page=131&per_page=5>; rel=\"last\", <https://api.github.com/repos/rails/rails/issues?page=1&per_page=5>; rel=\"first\", <https://api.github.com/repos/rails/rails/issues?page=2&per_page=5>; rel=\"prev\"");
                var app = Substitute.For<IApplication>();
                app.Invoke(env).Returns(Task.FromResult(app));
                var parser = new ApiInfoParser(app);

                await parser.Invoke(env);

                var i = ((GitHubResponse<string>)env.Response).ApiInfo;
                i.Should().NotBeNull();
                i.Links.Count.Should().Be(4);
                i.Links.ContainsKey("next").Should().BeTrue();
                i.Links["next"].Should().Be(new Uri("https://api.github.com/repos/rails/rails/issues?page=4&per_page=5"));
                i.Links.ContainsKey("prev").Should().BeTrue();
                i.Links["prev"].Should().Be(new Uri("https://api.github.com/repos/rails/rails/issues?page=2&per_page=5"));
                i.Links.ContainsKey("first").Should().BeTrue();
                i.Links["first"].Should().Be(new Uri("https://api.github.com/repos/rails/rails/issues?page=1&per_page=5"));
                i.Links.ContainsKey("last").Should().BeTrue();
                i.Links["last"].Should().Be(new Uri("https://api.github.com/repos/rails/rails/issues?page=131&per_page=5"));
            }

            [Fact]
            public async Task DoesNothingIfResponseIsntGitHubResponse()
            {
                var env = new Environment<string> { Response = new GitHubResponse<string>() };
                var app = Substitute.For<IApplication>();
                app.Invoke(env).Returns(Task.FromResult(app));
                var parser = new ApiInfoParser(app);

                await parser.Invoke(env);

                env.Response.Should().NotBeNull();
            }
        }

        public class ThePageUrlMethods
        {
            [Theory]
            [PropertyData("PagingMethods")]
            public void RetrievesTheCorrectPagePage(string linkName, Func<ApiInfo, Uri> pagingMethod)
            {
                var pageUri = new Uri("https://api.github.com/user/repos?page=2");
                var links = new Dictionary<string, Uri>
                {
                    { "garbage", new Uri("https://api.github.com/user/repos?page=1") },
                    { linkName, pageUri }
                };
                var info = BuildApiInfo(links);
                pagingMethod(info).Should().BeSameAs(pageUri);
            }

            [Theory]
            [PropertyData("PagingMethods")]
            public void ReturnsNullIfThereIsNoMatchingPagingLink(string ignored, Func<ApiInfo, Uri> pagingMethod)
            {
                var links = new Dictionary<string, Uri>();
                var info = BuildApiInfo(links);

                pagingMethod(info).Should().BeNull();
            }

            public static IEnumerable<object[]> PagingMethods 
            { 
                get
                {
                    yield return new object[] { "first", new Func<ApiInfo, Uri>(info => info.GetFirstPageUrl()) };
                    yield return new object[] { "last", new Func<ApiInfo, Uri>(info => info.GetLastPageUrl()) };
                    yield return new object[] { "prev", new Func<ApiInfo, Uri>(info => info.GetPreviousPageUrl()) };
                    yield return new object[] { "next", new Func<ApiInfo, Uri>(info => info.GetNextPageUrl()) };
                }
            }

            static ApiInfo BuildApiInfo(IDictionary<string, Uri> links)
            {
                return new ApiInfo(links, new List<string>(), new List<string>(), "etag", 0, 0);
            }
        }
    }
}