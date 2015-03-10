﻿using CollectionsOnline.WebSite.Models;
using CollectionsOnline.WebSite.Queries;
using Nancy.ModelBinding;

namespace CollectionsOnline.WebSite.Modules.Api
{
    public class SearchApiModule : BaseApiModule
    {
        public SearchApiModule(ISearchViewModelQuery searchViewModelQuery)
            : base("/search")
        {
            Get["search-api", ""] = parameters =>
            {
                var searchInputModel = this.Bind<SearchInputModel>();

                searchInputModel.CurrentUrl = string.Format("{0}{1}", Request.Url.SiteBase, Request.Url.Path);
                searchInputModel.CurrentQueryString = Request.Url.Query;

                return BuildResponse(searchViewModelQuery.BuildSearch(searchInputModel));
            };
        }
    }
}