import axios from "axios";

export const api = axios.create({
  baseURL: "/",
  withXSRFToken: false,
  responseType: "json",
  // Repeated values (a=1&a=2), the form ASP.NET binds, not a[]=1&a[]=2.
  paramsSerializer: { indexes: null },
  transitional: { silentJSONParsing: false },
});
