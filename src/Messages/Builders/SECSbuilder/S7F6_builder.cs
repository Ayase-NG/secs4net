using Secs4Net;
using SECSdata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SECSbuilder
{
    public static class S7F6_builder
    {
        public static SecsMessage Build(S7F6_data data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return new SecsMessage(7, 6, replyExpected: true)
            {
                Name = "RecipeData",
                SecsItem = Item.L(
                    Item.A(data.recipeName ?? string.Empty),
                    Item.B(data.recipeContent ?? Array.Empty<byte>())
                )
            };
        }
    }
}
