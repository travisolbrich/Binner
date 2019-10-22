﻿using Binner.Common.Integrations;
using Binner.Common.Models;
using Binner.Common.StorageProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Binner.Common.Services
{
    public class PartService : IPartService
    {
        private IStorageProvider _storageProvider;
        private OctopartApi _octopartApi;
        private DigikeyApi _digikeyApi;
        private MouserApi _mouserApi;
        private RequestContextAccessor _requestContext;

        public PartService(IStorageProvider storageProvider, RequestContextAccessor requestContextAccessor, OctopartApi octopartApi, DigikeyApi digikeyApi, MouserApi mouserApi)
        {
            _storageProvider = storageProvider;
            _requestContext = requestContextAccessor;
            _octopartApi = octopartApi;
            _digikeyApi = digikeyApi;
            _mouserApi = mouserApi;
        }

        public async Task<ICollection<SearchResult<Part>>> FindPartsAsync(string keywords)
        {
            return await _storageProvider.FindPartsAsync(keywords, _requestContext.GetUserContext());
        }

        public async Task<Part> GetPartAsync(string partNumber)
        {
            return await _storageProvider.GetPartAsync(partNumber, _requestContext.GetUserContext());
        }

        public async Task<ICollection<Part>> GetPartsAsync(PaginatedRequest request)
        {
            return await _storageProvider.GetPartsAsync(request, _requestContext.GetUserContext());
        }

        public async Task<Part> AddPartAsync(Part part)
        {
            return await _storageProvider.AddPartAsync(part, _requestContext.GetUserContext());
        }

        public async Task<Part> UpdatePartAsync(Part part)
        {
            return await _storageProvider.UpdatePartAsync(part, _requestContext.GetUserContext());
        }

        public async Task<bool> DeletePartAsync(Part part)
        {
            return await _storageProvider.DeletePartAsync(part, _requestContext.GetUserContext());
        }

        public async Task<PartType> GetOrCreatePartTypeAsync(PartType partType)
        {
            if (partType == null) throw new ArgumentNullException(nameof(partType));
            if (partType.Name == null) throw new ArgumentNullException(nameof(partType.Name));
            return await _storageProvider.GetOrCreatePartTypeAsync(partType, _requestContext.GetUserContext());
        }

        public async Task<ICollection<PartType>> GetPartTypesAsync()
        {
            return await _storageProvider.GetPartTypesAsync(_requestContext.GetUserContext());
        }

        public async Task<PartResults> GetPartInformationAsync(string partNumber, string partType = "", string packageType = "")
        {
            var datasheets = new List<string>();
            var response = new PartResults();
            var digikeyResponse = new Integrations.Models.Digikey.KeywordSearchResponse();
            var searchKeywords = partNumber;
            ICollection<Integrations.Models.Mouser.MouserPart> mouserParts = new List<Integrations.Models.Mouser.MouserPart>();
            if (_octopartApi.IsConfigured)
                datasheets.AddRange(await _octopartApi.GetDatasheetsAsync(partNumber));
            if (_digikeyApi.IsConfigured)
                digikeyResponse = await _digikeyApi.GetPartsAsync(searchKeywords, partType, packageType);
            if (_mouserApi.IsConfigured)
                mouserParts = await _mouserApi.GetPartsAsync(searchKeywords, partType, packageType);

            foreach(var part in digikeyResponse.Products)
            {
                var additionalPartNumbers = new List<string>();
                var basePart = part.Parameters.Where(x => x.Parameter.Equals("Base Part Number")).Select(x => x.Value).FirstOrDefault();
                if (!string.IsNullOrEmpty(basePart))
                    additionalPartNumbers.Add(basePart);
                response.Parts.Add(new CommonPart
                {
                    Supplier = "DigiKey",
                    SupplierPartNumber = part.DigiKeyPartNumber,
                    BasePartNumber = basePart,
                    AdditionalPartNumbers = additionalPartNumbers,
                    Manufacturer = part.Manufacturer.Value,
                    ManufacturerPartNumber = part.ManufacturerPartNumber,
                    Cost = part.UnitPrice,
                    Currency = digikeyResponse.SearchLocaleUsed.Currency,
                    DataSheetUrls = new List<string> { part.PrimaryDatasheet },
                    Description = part.ProductDescription + "\r\n" + part.DetailedDescription,
                    ImageUrl = part.PrimaryPhoto,
                    Package = part.Parameters
                        ?.Where(x => x.Parameter.Equals("Package / Case", StringComparison.InvariantCultureIgnoreCase))
                        .Select(x => x.Value)
                        .FirstOrDefault(),
                    MountingType = part.Parameters.Where(x => x.Parameter.Equals("Mounting Type")).Select(x => x.Value).FirstOrDefault(),
                    PartType = "",
                    ProductUrl = part.ProductUrl,
                    Status = part.ProductStatus
                });
            }

            foreach (var part in mouserParts)
            {
                response.Parts.Add(new CommonPart
                {
                    Supplier = "Mouser",
                    SupplierPartNumber = part.MouserPartNumber,
                    BasePartNumber = "",
                    Manufacturer = part.Manufacturer,
                    ManufacturerPartNumber = part.ManufacturerPartNumber,
                    Cost = part.PriceBreaks?.OrderBy(x => x.Quantity).FirstOrDefault()?.Cost ?? 0,
                    Currency = part.PriceBreaks?.OrderBy(x => x.Quantity).FirstOrDefault()?.Currency,
                    DataSheetUrls = new List<string> { part.DataSheetUrl },
                    Description = part.Description,
                    ImageUrl = part.ImagePath,
                    Package = "",
                    MountingType = "",
                    PartType = "",
                    ProductUrl = part.ProductDetailUrl,
                    Status = part.LifecycleStatus
                });
            }

            var partTypes = await _storageProvider.GetPartTypesAsync(_requestContext.GetUserContext());
            foreach (var part in response.Parts)
            {
                part.PartType = DeterminePartType(part, partTypes);
                part.Keywords = DetermineKeywords(part, partTypes);
            }

            return response;
        }

        public async Task<PartMetadata> GetPartMetadataAsync(string partNumber)
        {
            var dataSheets = new List<string>();
            var digikeyResponse = new Integrations.Models.Digikey.KeywordSearchResponse();
            ICollection<Integrations.Models.Mouser.MouserPart> mouserParts = new List<Integrations.Models.Mouser.MouserPart>();
            if (_octopartApi.IsConfigured)
                dataSheets.AddRange(await _octopartApi.GetDatasheetsAsync(partNumber));
            if (_digikeyApi.IsConfigured)
                digikeyResponse = await _digikeyApi.GetPartsAsync(partNumber);
            if (_mouserApi.IsConfigured)
                mouserParts = await _mouserApi.GetPartsAsync(partNumber);

            // find the most appropriate listing for each api
            var digikeyPart = digikeyResponse
                .Products
                .Where(x => !string.IsNullOrEmpty(x.PrimaryDatasheet))
                .OrderByDescending(x => x.QuantityAvailable)
                .FirstOrDefault();
            var mouserPart = mouserParts
                .Where(x => !string.IsNullOrEmpty(x.DataSheetUrl))
                .OrderByDescending(x => x.AvailabilityInteger)
                .FirstOrDefault();

            // determine lowest price information
            var digikeyPrice = digikeyPart?.UnitPrice;
            var mouserLowestQuantityPrice = mouserPart?.PriceBreaks?.OrderBy(x => x.Quantity).FirstOrDefault();
            var mouserPrice = mouserLowestQuantityPrice?.Cost;
            decimal? lowestCost = 0M;
            var lowestCostSupplier = "";
            var lowestCostCurrency = "";
            var lowestCostProductUrl = "";
            if (digikeyPrice <= mouserPrice)
            {
                lowestCostSupplier = "DigiKey";
                lowestCost = digikeyPrice;
                lowestCostCurrency = digikeyResponse?.SearchLocaleUsed?.Currency;
                lowestCostProductUrl = digikeyPart?.ProductUrl;
            }
            else
            {
                lowestCostSupplier = "Mouser";
                lowestCost = mouserPrice;
                lowestCostCurrency = mouserLowestQuantityPrice?.Currency;
                lowestCostProductUrl = mouserPart?.ProductDetailUrl;
            }

            // append all datasheets
            dataSheets.AddRange(digikeyResponse.Products.Select(x => x.PrimaryDatasheet).ToList());
            dataSheets.AddRange(mouserParts.Select(x => x.DataSheetUrl).ToList());

            // no results found at all
            if (digikeyPart == null && mouserPart == null && !dataSheets.Any())
                return null;

            // todo: cache datasheets and images when downloaded on datasheets.binner.io

            var metadata = new PartMetadata()
            {
                PartNumber = partNumber,
                DigikeyPartNumber = digikeyPart?.DigiKeyPartNumber,
                MouserPartNumber = mouserPart?.MouserPartNumber,
                DatasheetUrl = digikeyPart?.PrimaryDatasheet ?? mouserPart?.DataSheetUrl ?? dataSheets.FirstOrDefault(),
                AdditionalDatasheets = dataSheets.Distinct().ToList(),
                Description = digikeyPart?.ProductDescription ?? mouserPart?.Description,
                ManufacturerPartNumber = digikeyPart?.ManufacturerPartNumber ?? mouserPart?.ManufacturerPartNumber,
                DetailedDescription = digikeyPart?.DetailedDescription,
                Cost = lowestCost,
                Currency = lowestCostCurrency,
                LowestCostSupplier = lowestCostSupplier,
                LowestCostSupplierUrl = lowestCostProductUrl,
                Package = digikeyPart?.Parameters
                        ?.Where(x => x.Parameter.Equals("Package / Case", StringComparison.InvariantCultureIgnoreCase))
                        .Select(x => x.Value)
                        .FirstOrDefault(),
                Manufacturer = digikeyPart?.Manufacturer?.Value ?? mouserPart?.Manufacturer,
                ProductStatus = digikeyPart?.ProductStatus ?? mouserPart?.LifecycleStatus,
                ProductUrl = digikeyPart?.ProductUrl ?? mouserPart?.ProductDetailUrl,
                ImageUrl = digikeyPart?.PrimaryPhoto ?? mouserPart.ImagePath,
                Integrations = new Models.Integrations
                {
                    Digikey = digikeyPart,
                    Mouser = mouserPart,
                    AliExpress = null
                }
            };
            var partTypes = await _storageProvider.GetPartTypesAsync(_requestContext.GetUserContext());
            metadata.PartType = DeterminePartType(metadata, partTypes);
            metadata.Keywords = DetermineKeywords(metadata, partTypes);
            return metadata;
        }

        private ICollection<string> DetermineKeywords(PartMetadata metadata, ICollection<PartType> partTypes)
        {
            // part type
            // important parts from description
            // alternate series numbers etc
            var keywords = new List<string>();
            var possiblePartTypes = GetMatchingPartTypes(metadata, partTypes);
            foreach (var possiblePartType in possiblePartTypes)
                if (!keywords.Contains(possiblePartType.Key.Name, StringComparer.InvariantCultureIgnoreCase))
                    keywords.Add(possiblePartType.Key.Name.ToLower());

            if (!keywords.Contains(metadata.ManufacturerPartNumber, StringComparer.InvariantCultureIgnoreCase))
                keywords.Add(metadata.ManufacturerPartNumber.ToLower());
            var desc = metadata.Description.ToLower().Split(new string[] { " ", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            // add the first 4 words of desc
            var wordCount = 0;
            var ignoredWords = new string[] { "and", "the", "in", "or", "in", "a", };
            foreach (var word in desc)
            {
                if (!ignoredWords.Contains(word, StringComparer.InvariantCultureIgnoreCase) && !keywords.Contains(word, StringComparer.InvariantCultureIgnoreCase))
                {
                    keywords.Add(word.ToLower());
                    wordCount++;
                }
                if (wordCount >= 4)
                    break;
            }
            var basePart = metadata.Integrations?.Digikey?.Parameters.Where(x => x.Parameter.Equals("Base Part Number")).Select(x => x.Value).FirstOrDefault();
            if (basePart != null && !keywords.Contains(basePart, StringComparer.InvariantCultureIgnoreCase))
                keywords.Add(basePart.ToLower());
            var mountingType = metadata.Integrations?.Digikey?.Parameters.Where(x => x.Parameter.Equals("Mounting Type")).Select(x => x.Value).FirstOrDefault();
            if (mountingType != null && !keywords.Contains(mountingType, StringComparer.InvariantCultureIgnoreCase))
                keywords.Add(mountingType.ToLower());

            return keywords.Distinct().ToList();
        }

        private ICollection<string> DetermineKeywords(CommonPart part, ICollection<PartType> partTypes)
        {
            // part type
            // important parts from description
            // alternate series numbers etc
            var keywords = new List<string>();
            var possiblePartTypes = GetMatchingPartTypes(part, partTypes);
            foreach (var possiblePartType in possiblePartTypes)
                if (!keywords.Contains(possiblePartType.Key.Name, StringComparer.InvariantCultureIgnoreCase))
                    keywords.Add(possiblePartType.Key.Name.ToLower());

            if (!keywords.Contains(part.ManufacturerPartNumber, StringComparer.InvariantCultureIgnoreCase))
                keywords.Add(part.ManufacturerPartNumber.ToLower());
            var desc = part.Description.ToLower().Split(new string[] { " ", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            // add the first 4 words of desc
            var wordCount = 0;
            var ignoredWords = new string[] { "and", "the", "in", "or", "in", "a", };
            foreach (var word in desc)
            {
                if (!ignoredWords.Contains(word, StringComparer.InvariantCultureIgnoreCase) && !keywords.Contains(word, StringComparer.InvariantCultureIgnoreCase))
                {
                    keywords.Add(word.ToLower());
                    wordCount++;
                }
                if (wordCount >= 4)
                    break;
            }
            foreach(var basePart in part.AdditionalPartNumbers)
                if (basePart != null && !keywords.Contains(basePart, StringComparer.InvariantCultureIgnoreCase))
                    keywords.Add(basePart.ToLower());
            var mountingType = part.MountingType;
            if (!string.IsNullOrEmpty(mountingType) && !keywords.Contains(mountingType, StringComparer.InvariantCultureIgnoreCase))
                keywords.Add(mountingType.ToLower());

            return keywords.Distinct().ToList();
        }

        private IDictionary<PartType, int> GetMatchingPartTypes(PartMetadata metadata, ICollection<PartType> partTypes)
        {
            // load all part types
            var possiblePartTypes = new Dictionary<PartType, int>();
            foreach (var partType in partTypes)
            {
                if (string.IsNullOrEmpty(partType.Name))
                    continue;
                var addPart = false;
                if (metadata.Description?.IndexOf(partType.Name, StringComparison.InvariantCultureIgnoreCase) >= 0)
                    addPart = true;
                if (metadata.DetailedDescription?.IndexOf(partType.Name, StringComparison.InvariantCultureIgnoreCase) >= 0)
                    addPart = true;
                if (metadata.PartNumber?.IndexOf(partType.Name, StringComparison.InvariantCultureIgnoreCase) >= 0)
                    addPart = true;
                if (metadata.DatasheetUrl?.IndexOf(partType.Name, StringComparison.InvariantCultureIgnoreCase) >= 0)
                    addPart = true;
                if (addPart)
                {
                    if (possiblePartTypes.ContainsKey(partType))
                        possiblePartTypes[partType]++;
                    else
                        possiblePartTypes.Add(partType, 1);
                }

            }
            return possiblePartTypes;
        }

        private IDictionary<PartType, int> GetMatchingPartTypes(CommonPart part, ICollection<PartType> partTypes)
        {
            // load all part types
            var possiblePartTypes = new Dictionary<PartType, int>();
            foreach (var partType in partTypes)
            {
                if (string.IsNullOrEmpty(partType.Name))
                    continue;
                var addPart = false;
                if (part.Description?.IndexOf(partType.Name, StringComparison.InvariantCultureIgnoreCase) >= 0)
                    addPart = true;
                if (part.ManufacturerPartNumber?.IndexOf(partType.Name, StringComparison.InvariantCultureIgnoreCase) >= 0)
                    addPart = true;
                foreach(var datasheet in part.DataSheetUrls)
                    if (datasheet.IndexOf(partType.Name, StringComparison.InvariantCultureIgnoreCase) >= 0)
                        addPart = true;
                if (addPart)
                {
                    if (possiblePartTypes.ContainsKey(partType))
                        possiblePartTypes[partType]++;
                    else
                        possiblePartTypes.Add(partType, 1);
                }

            }
            return possiblePartTypes;
        }

        private string DeterminePartType(CommonPart part, ICollection<PartType> partTypes)
        {
            var possiblePartTypes = GetMatchingPartTypes(part, partTypes);
            return possiblePartTypes
                .OrderByDescending(x => x.Value)
                .Select(x => x.Key?.Name)
                .FirstOrDefault();
        }

        private string DeterminePartType(PartMetadata metadata, ICollection<PartType> partTypes)
        {
            var possiblePartTypes = GetMatchingPartTypes(metadata, partTypes);
            return possiblePartTypes
                .OrderByDescending(x => x.Value)
                .Select(x => x.Key?.Name)
                .FirstOrDefault();
        }
    }
}
