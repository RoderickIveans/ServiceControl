﻿namespace ServiceControl.Audit.Infrastructure
{
    using System.Net.Http;

    public class PagingInfo
    {
        public const int DefaultPageSize = 50;

        public int Page { get; }
        public int PageSize { get; }
        public int Offset { get; }
        public int Next { get; }

        public PagingInfo(int page = 1, int pageSize = DefaultPageSize)
        {
            Page = page;
            PageSize = pageSize;
            Next = pageSize;
            Offset = (Page - 1) * Next;
        }
    }

    public static class PagingInfoExtension
    {
        public static PagingInfo GetPagingInfo(this HttpRequestMessage request)
        {
            var maxResultsPerPage = request.GetQueryStringValue("per_page", PagingInfo.DefaultPageSize);
            if (maxResultsPerPage < 1)
            {
                maxResultsPerPage = PagingInfo.DefaultPageSize;
            }

            var page = request.GetQueryStringValue("page", 1);
            if (page < 1)
            {
                page = 1;
            }

            return new PagingInfo(page, maxResultsPerPage);
        }
    }

    public static class IncludeSystemMessageExtension
    {
        public static bool GetIncludeSystemMessages(this HttpRequestMessage request)
        {
            var includeSystemMessages = request.GetQueryStringValue("include_system_messages", false);

            return includeSystemMessages;
        }
    }
}