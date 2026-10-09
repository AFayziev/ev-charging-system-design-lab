import axios from "axios";

export const api = axios.create({
  baseURL: "/",
  withXSRFToken: false,
  responseType: "json",
  transitional: { silentJSONParsing: false },
});
