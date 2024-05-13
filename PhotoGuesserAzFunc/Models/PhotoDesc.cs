namespace PhotoGuesser.Data;

public record PhotoDesc(
    string webUrl, string imageUrl, int year, 
    string title, double latitude, double longtitude);