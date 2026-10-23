namespace Clinisys.Domain;

public enum Sex { Unspecified = 0, Female = 1, Male = 2 }
public enum RequestStatus { Registered = 0, InProgress = 1, ResultsEntered = 2, Validated = 3, Delivered = 4 }
public enum Abnormality { NotEvaluated = 0, Normal = 1, Low = 2, High = 3 }
public enum AuditAction { Created = 0, Updated = 1, StatusChanged = 2, Validated = 3, Delivered = 4 }
