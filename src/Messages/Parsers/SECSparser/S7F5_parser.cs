using Secs4Net;
using SECSdata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSparser
{
    public static class S7F5_parser
    {
        public static S7F5_data Parse(SecsMessage message)
        {
            if (message.S != 7 || message.F != 5)
            {
                throw new ArgumentException("Invalid message type. Expected S7F5.");
            }
            var items = message.SecsItem;
            if (items.Count < 1)
            {
                throw new ArgumentNullException("S7F5 message does not contain any items.");
            }
            var recipeNameItem = items;
            if (recipeNameItem.Format != SecsFormat.ASCII)
            {
                throw new ArgumentOutOfRangeException("S7F5 message's first item is not of type ASCII.");
            }
            var recipeName = recipeNameItem.GetString();
            return new S7F5_data { recipeName = recipeName };
        }
    }
}
