using CivicBudget.Web.Components.Common;
using Microsoft.AspNetCore.Components.Forms;

namespace CivicBudget.Web.Tests;

/// <summary>
/// The password field is still an ordinary, bindable password input, and its show/hide button is
/// wired to it by id so the script and assistive tech both know which field it reveals.
/// </summary>
public class PasswordInputTests : BunitContext
{
    private sealed class Model
    {
        public string Password { get; set; } = "";
    }

    [Fact]
    public void Renders_a_password_input_with_a_labelled_toggle_that_names_the_field()
    {
        var model = new Model();

        IRenderedComponent<EditForm> form = Render<EditForm>(p => p
            .Add(f => f.Model, model)
            .Add(f => f.ChildContent, _ => b =>
            {
                b.OpenComponent<PasswordInput>(0);
                b.AddAttribute(1, "id", "pw");
                b.AddAttribute(2, "class", "form-control");
                b.AddAttribute(3, nameof(PasswordInput.Value), model.Password);
                b.AddAttribute(4, nameof(PasswordInput.ValueChanged), Microsoft.AspNetCore.Components.EventCallback.Factory.Create<string>(this, v => model.Password = v));
                b.AddAttribute(5, nameof(PasswordInput.ValueExpression), (System.Linq.Expressions.Expression<Func<string>>)(() => model.Password));
                b.CloseComponent();
            }));

        AngleSharp.Dom.IElement input = form.Find("input#pw");
        Assert.Equal("password", input.GetAttribute("type"));
        Assert.Contains("form-control", input.GetAttribute("class"));

        AngleSharp.Dom.IElement toggle = form.Find("button[data-password-toggle]");
        Assert.Equal("button", toggle.GetAttribute("type")); // never submits the form
        Assert.Equal("pw", toggle.GetAttribute("aria-controls"));
        Assert.Equal("Show password", toggle.GetAttribute("aria-label"));

        input.Change("s3cret-Passw0rd!");
        Assert.Equal("s3cret-Passw0rd!", model.Password);
    }
}
