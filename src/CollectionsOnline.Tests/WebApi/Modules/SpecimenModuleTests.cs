﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CollectionsOnline.Core.Models;
using CollectionsOnline.WebApi.Modules;
using Nancy;
using Nancy.Testing;
using Shouldly;
using WorldDomination.Raven.Tests.Helpers;
using Xunit;

namespace CollectionsOnline.Tests.WebApi.Modules
{
    public class SpecimenModuleTests : RavenDbTestBase
    {
        public SpecimenModuleTests()
        {
            DataToBeSeeded = new List<IEnumerable>
                {
                    FakeSpecimens.CreateFakeSpecimens(5)
                };

            Browser = new Browser(with =>
                {
                    with.Module<SpecimenModule>();
                    with.Dependency(DocumentSession);
                });
        }

        protected Browser Browser { get; set; }

        [Fact]
        public void GetSpecimens_ReturnsSpecimens()
        {
            var result = Browser.Get("/v1/specimens", with => with.HttpRequest());

            result.Body.DeserializeJson<IEnumerable<Specimen>>().Count().ShouldBe(5);
        }

        [Fact]
        public void GivenAnInvalidId_GetSpecies_ReturnsNotFound()
        {
            var result = Browser.Get("/v1/specimens/6", with => with.HttpRequest());

            result.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }
}