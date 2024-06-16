﻿using App.Context.Models;
using MassTransit;
using OpenAI.Utilities.FunctionCalling;
using System;
using System.Globalization;
using System.Text.Json;

namespace ParkSharing.Services.ChatGPT
{
    public class ChatGPTCapabilities
    {
        IReservationService _reservation;
        IBus _messageBroker;
        public ChatGPTCapabilities(IReservationService reservation, IBus messageBroker)
        {
            _reservation = reservation;
            _messageBroker = messageBroker;
        }

        [FunctionDescription("Rezervace parkovacího místa. Neni dovoleno rezervova na delsi dobu nez 3 dny, neni dovoleno rezervovat misto pokud neni volne. Navratova hodnota je Nazev parkovaciho mista a celkova cena. Rezervovat lze jen volna mista ziskane funkci AvaliableSpots. Sam vyber nahodne nektere misto. Povolene jsou rezervovat jen cele hodiny")]
        public async Task<string> ReserveSpot(
            [ParameterDescription("Datetime format yyyy-mm-dd HH:00")] string from,
            [ParameterDescription("Datetime format yyyy-mm-dd HH:00")] string to, 
            string spotName,
            [ParameterDescription("Telefon pro kontakt")] string phone)
        {
            if (!TryParseDateTime(from, out DateTime fromDateTime))
            {
                return "Invalid 'from' date format.";
            }

            if (!TryParseDateTime(to, out DateTime toDateTime))
            {
                return "Invalid 'to' date format.";
            }

            var spot = await _reservation.GetParkingSpotByNameAsync(spotName);

            var totalPrice = spot.PricePerHour * (toDateTime - fromDateTime).Hours;

            var id = Guid.NewGuid().ToString();
            var result = await _reservation.ReserveAsync(spot.Name, new ReservationSpot()
            {
                Phone = phone,
                End = toDateTime,
                Start = fromDateTime,
                Price = (int)totalPrice,
                PublicId = id
            });

            if(result == false)
            {
                return $"Reservation not created, spot is already reserved for this time.";
            }

            return $"Reservation created TotalPrice:{totalPrice} BankAccount To pay:{spot.BankAccount}";
        }

        [FunctionDescription("Tato metoda vrací možné volné termíny a jejich cenu za hodinu. Povolene jsou jen cele hodiny, například od 13:00 do 15:00. Pokud je zdarma napiš to.")]
        public async Task<string> GetAllOpenSlots(
          [ParameterDescription("Datetime format yyyy-mm-dd HH:00")] string from,
          [ParameterDescription("Datetime format yyyy-mm-dd HH:00")] string to)
        {
            if (!TryParseDateTime(from, out DateTime fromDateTime))
            {
                return "Invalid 'from' date format.";
            }

            if (!TryParseDateTime(to, out DateTime toDateTime))
            {
                return "Invalid 'to' date format.";
            }

            if ((toDateTime - fromDateTime).Days > 3)
            {
                return "From - to range is too big. Max search range 4 days.";
            }

            var freeSlots = await _reservation.GetAllOpenSlots(fromDateTime, toDateTime);
            var cetZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

           

            var res = freeSlots.Select(f =>
            {
                var fromCET = TimeZoneInfo.ConvertTimeFromUtc(f.From, cetZone);
                var toCET = TimeZoneInfo.ConvertTimeFromUtc(f.To, cetZone);
                return $"{fromCET.ToString("dd MMM yyyy HH:mm")}-{toCET.ToString("dd MMM yyyy HH:mm")},{f.SpotName},PricePerHour:{f.PricePerHour}:";
            }).ToList();

            if(res.Count == 0)
            {
                return "Not found";
            }

            return string.Join('\n',res);
        }

        private bool TryParseDateTime(string input, out DateTime dateTime)
        {
            string[] formats = { "yyyy-MM-dd HH:00" };
            return DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime);
        }


        [FunctionDescription("Detail o parkovacim miste. Zobrazit vzdy pri potvrzeni rezervace")]
        public async Task<string> SpotDetail(string spot)
        {
            // Sanitize the input
            if (!Guid.TryParse(spot, out Guid spotGuid))
            {
                return JsonSerializer.Serialize(new { error = "Invalid spot identifier." });
            }

            // Get the parking spot details
            var parkingSpot = await _reservation.GetParkingSpotAsync(spotGuid);
            if (parkingSpot == null)
            {
                return JsonSerializer.Serialize(new { error = "Parking spot not found." });
            }

            // Prepare the result
            var result = new
            {
                Name = parkingSpot.Name,
                PricePerHour = parkingSpot.PricePerHour
            };

            // Serialize the result to JSON
            return JsonSerializer.Serialize(result);
        }
    }

    public class SpotDetails
    {
        public string Name { get; set; }
        public string BankAccount { get; set; }
        public string PricePerHour { get; set; }
    }
}
