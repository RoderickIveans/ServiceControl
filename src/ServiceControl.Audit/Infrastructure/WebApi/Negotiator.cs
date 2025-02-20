namespace ServiceControl.Audit.Infrastructure.WebApi
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using QueryResult = Auditing.MessagesView.QueryResult;

    static class Negotiator
    {
        public static HttpResponseMessage FromQueryResult(HttpRequestMessage request, QueryResult queryResult, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var response = request.CreateResponse(statusCode, queryResult.DynamicResults);
            var queryStats = queryResult.QueryStats;

            return response.WithPagingLinksAndTotalCount(queryStats.TotalCount, queryStats.HighestTotalCountOfAllTheInstances, request)
                .WithDeterministicEtag(queryStats.ETag);
        }

        public static HttpResponseMessage WithPagingLinksAndTotalCount(this HttpResponseMessage response, int totalCount, int highestTotalCountOfAllInstances,
            HttpRequestMessage request)
        {
            return response.WithTotalCount(totalCount)
                .WithPagingLinks(totalCount, highestTotalCountOfAllInstances, request);
        }

        public static HttpResponseMessage WithTotalCount(this HttpResponseMessage response, int total)
        {
            return response.WithHeader("Total-Count", total.ToString(CultureInfo.InvariantCulture));
        }

        public static HttpResponseMessage WithPagingLinks(this HttpResponseMessage response, int totalResults, int highestTotalCountOfAllInstances, HttpRequestMessage request)
        {
            decimal maxResultsPerPage = 50;

            var queryNameValuePairs = request.GetQueryNameValuePairs().ToList();

            var per_pageParameter = queryNameValuePairs.LastOrDefault(x => x.Key == "per_page").Value;
            if (per_pageParameter != null)
            {
                maxResultsPerPage = decimal.Parse(per_pageParameter);
            }

            if (maxResultsPerPage < 1)
            {
                maxResultsPerPage = 50;
            }

            var page = 1;

            var pageParameter = queryNameValuePairs.LastOrDefault(x => x.Key == "page").Value;
            if (pageParameter != null)
            {
                page = int.Parse(pageParameter);
            }

            if (page < 1)
            {
                page = 1;
            }

            // No need to add a Link header if no paging
            if (totalResults <= maxResultsPerPage)
            {
                return response;
            }

            var links = new List<string>();
            var lastPage = (int)Math.Ceiling(highestTotalCountOfAllInstances / maxResultsPerPage);

            // No need to add a Link header if page does not exist!
            if (page > lastPage)
            {
                return response;
            }

            var path = request.GetRouteData().Route.RouteTemplate; //should ideally be request.RequestUri.AbsolutePath, but using route instead to keep it backwards compatible
            var query = new StringBuilder();

            query.Append("?");
            foreach (var pair in queryNameValuePairs.Where(pair => pair.Key != "page"))
            {
                query.AppendFormat("{0}={1}&", pair.Key, pair.Value);
            }

            var queryParams = query.ToString();

            if (page != 1)
            {
                AddLink(links, 1, "first", path, queryParams);
            }

            if (page > 1)
            {
                AddLink(links, page - 1, "prev", path, queryParams);
            }

            if (page != lastPage)
            {
                AddLink(links, lastPage, "last", path, queryParams);
            }

            if (page < lastPage)
            {
                AddLink(links, page + 1, "next", path, queryParams);
            }

            return response.WithHeader("Link", string.Join(", ", links));
        }

        static void AddLink(ICollection<string> links, int page, string rel, string uriPath, string queryParams)
        {
            var query = $"{queryParams}page={page}";

            links.Add($"<{uriPath + query}>; rel=\"{rel}\"");
        }

        public static HttpResponseMessage WithDeterministicEtag(this HttpResponseMessage response, string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return response;
            }

            return response.WithEtag(data);
        }

        public static HttpResponseMessage WithEtag(this HttpResponseMessage response, string etag)
        {
            response.Headers.ETag = new EntityTagHeaderValue($"\"{etag}\"");
            return response;
        }

        static HttpResponseMessage WithHeader(this HttpResponseMessage response, string header, string value)
        {
            response.Headers.Add(header, value);
            return response;
        }
    }
}