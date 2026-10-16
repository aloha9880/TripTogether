using System;
using System.Collections.Generic;
using System.Text;

namespace TripTogether.App
{
    public sealed class DateTimePickerPage : ContentPage
    {
        private readonly TaskCompletionSource<DateTime?> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool closing;

        public Task<DateTime?> Result => completion.Task;

        public DateTimePickerPage(
            string title,
            DateTime initialValue,
            bool allDay,
            string timeZoneId)
        {
            Title = title;

            var datePicker = new DatePicker
            {
                Date = initialValue.Date,
                Format = "MMM d, yyyy"
            };

            var timePicker = new TimePicker
            {
                Time = initialValue.TimeOfDay,
                Format = "h:mm tt",
                IsVisible = !allDay
            };

            SemanticProperties.SetDescription(datePicker, $"{title} date");
            SemanticProperties.SetDescription(timePicker, $"{title} time");

            var save = new Button { Text = "Continue" };
            var cancel = new Button { Text = "Cancel" };

            save.Clicked += async (_, _) =>
            {
                if (datePicker.Date is not DateTime date ||
                    (!allDay && timePicker.Time is null))
                {
                    await DisplayAlertAsync(
                        "Selection required",
                        "Select a date and time.",
                        "OK");
                    return;
                }

                var selected = DateTime.SpecifyKind(
                    date.Date.Add(allDay
                        ? TimeSpan.Zero
                        : timePicker.Time!.Value),
                    DateTimeKind.Unspecified);

                closing = true;
                save.IsEnabled = cancel.IsEnabled = false;

                await Navigation.PopModalAsync();
                completion.TrySetResult(selected);
            };

            cancel.Clicked += async (_, _) =>
            {
                closing = true;
                save.IsEnabled = cancel.IsEnabled = false;

                await Navigation.PopModalAsync();
                completion.TrySetResult(null);
            };

            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = 24,
                    Spacing = 16,
                    Children =
                {
                    new Label
                    {
                        Text = title,
                        FontSize = 24,
                        FontAttributes = FontAttributes.Bold
                    },
                    new Label
                    {
                        Text = allDay
                            ? "Choose a date."
                            : $"Time zone: {timeZoneId}"
                    },
                    datePicker,
                    timePicker,
                    save,
                    cancel
                }
                }
            };
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            // Handles dismissal using the device's Back button or gesture.
            Dispatcher.Dispatch(() =>
            {
                if (!closing && !Navigation.ModalStack.Contains(this))
                    completion.TrySetResult(null);
            });
        }
    }
}
