﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using ShipIt.Exceptions;
using ShipIt.Models.ApiModels;
using ShipIt.Repositories;

namespace ShipIt.Controllers
{
    [Route("orders/outbound")]
    public class OutboundOrderController : ControllerBase
    {
        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType);

        private readonly IStockRepository _stockRepository;
        private readonly IProductRepository _productRepository;

        public OutboundOrderController(IStockRepository stockRepository, IProductRepository productRepository)
        {
            _stockRepository = stockRepository;
            _productRepository = productRepository;
        }


        public List<String> GetGtins(IEnumerable<OrderLine> OrderLines)
        {

            return OrderLines.Select(line => line.gtin).ToList();
        }

        public void CheckDuplicateGtins(List<String> gtins)
        {
            var duplicateGtins = gtins.GroupBy(gtin => gtin).Where(group => group.Count() > 1).Select(group => group.Key);

            if (duplicateGtins.Any())
            {
                throw new ValidationException(String.Format("Outbound order request contains duplicate product gtin: {0}", duplicateGtins.First()));
            }
        }
        //grouping gtins that re the same, filter so that we just have gtins that idenitcal, then get the value of that gtin
        //gets a list of all duplicate gtin
        // public List<String> CheckUnkownGtins(IEnumerable<OrderLine> OrderLines)
        // {
        //     var ValidProduct = products.ContainsKey(GetGtins);
        //     // var InvalidProduct = !products.ContainsKey(GetGtins);
        //     if(ValidProduct) {
        //         var product = products[orderLine.gtin];
        //             lineItems.Add(new StockAlteration(product.Id, orderLine.quantity));
        //             productIds.Add(product.Id);

        //     }
        //     else{

        //     errors.Add(string.Format("Unknown product gtin: {0}", orderLine.gtin));
        //     }


        //     }

        [HttpPost("")]
        public void Post([FromBody] OutboundOrderRequestModel request)
        {
            Log.Info(String.Format("Processing outbound order: {0}", request));

            var gtins = GetGtins(request.OrderLines);
            CheckDuplicateGtins(gtins);

            var productDataModels = _productRepository.GetProductsByGtin(gtins);
            var products = productDataModels.ToDictionary(p => p.Gtin, p => new Product(p));

            var lineItems = new List<StockAlteration>();
            var productIds = new List<int>();
            var errors = new List<string>();


            foreach (var orderLine in request.OrderLines)
            {
                if (!products.ContainsKey(orderLine.gtin))
                {
                    errors.Add(string.Format("Unknown product gtin: {0}", orderLine.gtin));
                }
                else
                {
                    var product = products[orderLine.gtin];
                    lineItems.Add(new StockAlteration(product.Id, orderLine.quantity));
                    productIds.Add(product.Id);
                }
            }

            if (errors.Count > 0)
            {
                throw new NoSuchEntityException(string.Join("; ", errors));
            }

            var stock = _stockRepository.GetStockByWarehouseAndProductIds(request.WarehouseId, productIds);

            var orderLines = request.OrderLines.ToList();
            errors = new List<string>();

            for (int i = 0; i < lineItems.Count; i++)
            {
                var lineItem = lineItems[i];
                var orderLine = orderLines[i];

                if (!stock.ContainsKey(lineItem.ProductId))
                {
                    errors.Add(string.Format("Product: {0}, no stock held", orderLine.gtin));
                    continue;
                }

                var item = stock[lineItem.ProductId];
                if (lineItem.Quantity > item.held)
                {
                    errors.Add(
                        string.Format("Product: {0}, stock held: {1}, stock to remove: {2}", orderLine.gtin, item.held,
                            lineItem.Quantity));
                }
            }

            if (errors.Count > 0)
            {
                throw new InsufficientStockException(string.Join("; ", errors));
            }

            _stockRepository.RemoveStock(request.WarehouseId, lineItems);
        }
    }
}