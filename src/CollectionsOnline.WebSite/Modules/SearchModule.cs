﻿using CollectionsOnline.WebSite.Models;
using CollectionsOnline.WebSite.Queries;
using Nancy;
using Nancy.ModelBinding;

namespace CollectionsOnline.WebSite.Modules
{
    public class SearchModule : NancyModule
    {
        public SearchModule(
            ISearchViewModelQuery searchViewModelQuery)            
        {
            Get["/search"] = parameters =>
            {
                var searchInputModel = this.Bind<SearchInputModel>();

                searchInputModel.CurrentUrl = string.Format("{0}{1}", Request.Url.SiteBase, Request.Url.Path);
                searchInputModel.CurrentQueryString = Request.Url.Query;

                return View["search", searchViewModelQuery.BuildSearch(searchInputModel)];
            };
        }
    }
}