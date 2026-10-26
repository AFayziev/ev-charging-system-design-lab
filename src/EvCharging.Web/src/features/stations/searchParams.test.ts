import { describe, expect, it } from "vitest";
import {
  isSearching,
  readSearchFilters,
  writeSearchFilters,
} from "./searchParams";

describe("search parameters", () => {
  it("round-trips through the URL", () => {
    const parameters = new URLSearchParams(
      "q=north&type=Type2&type=CCS2&supply=DC&minKw=22&near=1&radius=25",
    );

    const filters = readSearchFilters(parameters);

    expect(filters).toEqual({
      text: "north",
      connectorTypes: ["Type2", "CCS2"],
      powerSupplies: ["DC"],
      minPowerKw: 22,
      nearMe: true,
      radiusKm: 25,
    });
    expect(writeSearchFilters(filters).toString()).toBe(parameters.toString());
  });

  it("keeps the default radius out of the URL and writes no radius without Near me", () => {
    expect(
      writeSearchFilters(
        readSearchFilters(new URLSearchParams("near=1")),
      ).toString(),
    ).toBe("near=1");
    expect(
      writeSearchFilters(
        readSearchFilters(new URLSearchParams("radius=any")),
      ).toString(),
    ).toBe("");
  });

  it("is not a search until there is text of the minimum length, a filter, or Near me", () => {
    const search = (query: string) =>
      isSearching(readSearchFilters(new URLSearchParams(query)));

    expect(search("")).toBe(false);
    expect(search("q=n")).toBe(false);
    expect(search("q=%20n%20")).toBe(false);
    expect(search("q=no")).toBe(true);
    expect(search("supply=AC")).toBe(true);
    expect(search("near=1")).toBe(true);
  });
});
